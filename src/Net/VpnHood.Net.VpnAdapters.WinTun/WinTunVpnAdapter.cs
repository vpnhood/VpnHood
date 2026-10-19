using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.WinTun.WinNative;

namespace VpnHood.Net.VpnAdapters.WinTun;

public class WinTunVpnAdapter(WinVpnAdapterSettings adapterSettings)
    : TunVpnAdapter(adapterSettings)
{
    private uint _adapterIndex;
    private readonly int _ringCapacity = adapterSettings.RingCapacity;
    private IntPtr _tunAdapter;
    private IntPtr _tunSession;
    private IntPtr _readEvent;
    private readonly byte[] _writeBuffer = new byte[0xFFFF];
    private static readonly Lock WinTunDllLock = new();
    private static IntPtr _winTunDll; // the one wintun.dll of the process, once loaded

    // _tunAdapter and _tunSession are raw native pointers freed by WintunCloseAdapter and
    // WintunEndSession; passing one after (or while) it is freed corrupts the process. This lock makes
    // AdapterOpen atomic against AdapterClose and AdapterRemove, which can run concurrently when the
    // user disconnects while a connection is still setting up the adapter.
    private readonly Lock _adapterLock = new();

    // Ending a session frees its rings, so the reader and the batch being sent are waited for first: a
    // stop pays it, never a packet. Past the limit the session is left allocated, never ended. The batch
    // may be asleep in the send backoff, whose longest sleep is under twice MaxPacketSendDelay. A thread
    // held off the CPU with the stop's own (a VM pause) resumes with it, so a late one gets a moment more.
    private static readonly TimeSpan StopWaitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopRewaitTimeout = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _senderStopWaitTimeout = StopWaitTimeout + adapterSettings.MaxPacketSendDelay * 2;

    // ERROR_ALREADY_EXISTS: plain when a create gave up on a removed process's device, as an HRESULT while
    // a live handle holds the adapter
    private const int ErrorAlreadyExists = 183;
    private const int ErrorAlreadyExistsHResult = unchecked((int)0x800700B7);

    public const int MinRingCapacity = 0x20000; // 128kiB
    public const int MaxRingCapacity = 0x4000000; // 64MiB
    protected override bool IsSocketProtectedByBind => true;
    // WinNAT translates IPv4 only: New-NetNat answers "IPV6 is not supported."
    public override bool IsNatSupported(IpVersion ipVersion) => ipVersion == IpVersion.IPv4;
    public override bool IsAppFilterSupported => false;
    protected override string? AppPackageId => null;
    // A client takes a new network without a restart, whose teardown would let the host's traffic pass
    // outside the tunnel for its seconds; a server restarts, as it adds its NAT only with a primary IP
    protected override bool RestartAfterNetworkAddressChanged => UseNat;
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => true;

    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on WinTun.");

    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on WinTun.");

    private static Guid BuildGuidFromName(string adapterName)
    {
        adapterName = $"VpnHood.{adapterName}"; // make sure it is unique
        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(adapterName));

        // Create 16 bytes array for GUID
        var guidBytes = new byte[16];
        Array.Copy(hashBytes, 0, guidBytes, 0, 16);

        // Set UUID version (5: SHA-1-based name-based UUID)
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50); // set version to 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // set variant to RFC 4122

        var adapterGuid = new Guid(guidBytes);
        return adapterGuid;
    }

    //public static int GetAdapterIndex(Guid adapterId)
    //{
    //    var adapter = NetworkInterface.GetAllNetworkInterfaces()
    //        .Single(x => Guid.TryParse(x.Id, out var id) && id == adapterId);

    //    var ipProps = adapter.GetIPProperties();
    //    var index = ipProps.GetIPv4Properties()?.Index ?? -1;
    //    return index;
    //}

    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        // Load the WinTun DLL
        LoadWinTunDll();

        // create the adapter. One of this name whose process ended a moment ago (a crash, a debugger's stop)
        // is still being removed: a create racing that removal gives up after WinTun's 15 s wait with the
        // plain code, by when that device is gone, so it is created once more
        _tunAdapter = WinTunApi.WintunCreateAdapter(AdapterName, "VPN", BuildGuidFromName(AdapterName));
        var errorCode = _tunAdapter == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        if (errorCode == ErrorAlreadyExists) {
            VhLogger.Instance.LogWarning(
                "The WinTun adapter {AdapterName} of an ended process was still being removed. Creating it again...",
                AdapterName);
            _tunAdapter = WinTunApi.WintunCreateAdapter(AdapterName, "VPN", BuildGuidFromName(AdapterName));
            errorCode = _tunAdapter == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        }

        // A live one is not ours to remove: WinTun removes an adapter only by the handle that created it.
        // Error 2 failed every create once, in the boot that first installed WinTun's driver (Windows 10,
        // CR_INVALID_DEVNODE in setupapi.dev.log; older builds too); a reboot cleared it, not seen since
        if (_tunAdapter == IntPtr.Zero)
            throw errorCode is ErrorAlreadyExists or ErrorAlreadyExistsHResult
                ? new PInvokeException(
                    $"The WinTun adapter {AdapterName} already exists: another process holds it. ErrorCode: {errorCode}",
                    errorCode)
                : new PInvokeException(
                    $"Failed to create WinTun adapter. Make sure the app is running with admin privilege. ErrorCode: {errorCode}",
                    errorCode);

        _adapterIndex = GetAdapterIndex(AdapterName);
        return Task.CompletedTask;
    }

    protected override void AdapterRemove()
    {
        lock (_adapterLock) {
            // A session ends before its adapter's close, never after: the close removes the device, whose
            // driver then closes the session's device handle, so a later end would close a handle value
            // the process may have reused. A live session may still exist here when AdapterOpen raced a
            // concurrent Stop and won, so it is closed here too.
            AdapterClose();

            // close the adapter
            if (_tunAdapter != IntPtr.Zero) {
                WinTunApi.WintunCloseAdapter(_tunAdapter);
                _tunAdapter = IntPtr.Zero;
            }
        }

        // WinNAT keeps the NAT after the process, so a removal that fails is worth a warning
        if (UseNat) {
            VhLogger.Instance.LogDebug("Removing the NAT of the {AdapterName} TUN adapter...", AdapterName);
            try {
                RemoveNat();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex,
                    "Could not remove the NAT {NatName}; the next start removes it.", NatName);
            }
        }

        _adapterIndex = 0;
    }

    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        // start WinTun session
        VhLogger.Instance.LogInformation("Starting WinTun session...");
        lock (_adapterLock) {
            // a disconnect during connect may have already removed the adapter; the freed native
            // handle must never reach WintunStartSession
            if (_tunAdapter == IntPtr.Zero)
                throw new InvalidOperationException("Could not start WinTun session. The adapter has been removed.");

            _tunSession = WinTunApi.WintunStartSession(_tunAdapter, _ringCapacity);
            if (_tunSession == IntPtr.Zero)
                throw new Win32Exception("Failed to start WinTun session.");

            // create an event object to wait for packets
            VhLogger.Instance.LogDebug("Creating event object for WinTun...");
            _readEvent = WinTunApi.WintunGetReadWaitEvent(_tunSession); // do not close this handle by documentation
        }

        return Task.CompletedTask;
    }

    protected override void AdapterClose()
    {
        lock (_adapterLock) {
            // The fields first, with a full fence: from here the reader and the sender find no session
            var session = Interlocked.Exchange(ref _tunSession, IntPtr.Zero);
            var readEvent = Interlocked.Exchange(ref _readEvent, IntPtr.Zero);
            if (session == IntPtr.Zero)
                return;

            // then what may still hold the session leaves before it is freed: the reader, woken from
            // its wait, and the batch being sent; a finalizer finds neither, as either keeps the adapter
            // alive. One that does not leave in time may still be inside a WinTun call, so the session is
            // left allocated, never freed under it; the adapter's removal closes its device handle and
            // ends its rings' use.
            if (!WaitForSessionThreads(readEvent))
                return;

            WinTunApi.WintunEndSession(session);
        }
    }

    // Whether the reader and the batch being sent have left the session
    private bool WaitForSessionThreads(IntPtr readEvent)
    {
        if (!Kernel32.SetEvent(readEvent))
            VhLogger.Instance.LogWarning(new Win32Exception(), "Could not wake the WinTun reader.");

        var readerEnded = WaitForReader(StopWaitTimeout);
        var senderEnded = WaitForSender(_senderStopWaitTimeout);
        readerEnded = readerEnded || WaitForReader(StopRewaitTimeout);
        senderEnded = senderEnded || WaitForSender(StopRewaitTimeout);
        if (readerEnded && senderEnded)
            return true;

        VhLogger.Instance.LogError(
            "The WinTun {Thread} did not leave the session in time, so it stays allocated, " +
            "{Size:F1} MiB, until the process exits.",
            senderEnded ? "reader" : readerEnded ? "sender" : "reader and sender",
            2 * (_ringCapacity + 0x10000) / 1048576.0);
        return false;
    }

    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        // not supported. ignore
        return Task.CompletedTask;
    }

    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        if (ipV4)
            Win32IpHelper.SetMetric(_adapterIndex, AddressFamily.InterNetwork, metric);

        if (ipV6)
            Win32IpHelper.SetMetric(_adapterIndex, AddressFamily.InterNetworkV6, metric);

        return Task.CompletedTask;
    }

    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        MigrateSavedAddresses(ipNetwork.AddressFamily);
#pragma warning restore CS0618 // Type or member is obsolete

        // no duplicate address detection: a tunnel shares its link with no one, and the detection would keep
        // the address unusable for seconds after the interface comes up, while the routes already lead to it
        Win32IpHelper.DisableDuplicateAddressDetection(_adapterIndex, ipNetwork.AddressFamily);
        Win32IpHelper.AddAddress(_adapterIndex, ipNetwork);
        return Task.CompletedTask;
    }

    // A netsh build saved its address for the adapter's GUID, and after its crash Windows brings that back
    // with the adapter, where it would be the traffic's source; netsh's static address replaced it, so the
    // family's addresses go first.
    [Obsolete("Migration (2026-10): drop a few months after it ships, with its call in AddAddress and " +
              "GetAdapterAddresses.")]
    private void MigrateSavedAddresses(AddressFamily addressFamily)
    {
        foreach (var ipAddress in GetAdapterAddresses(addressFamily))
            Win32IpHelper.DeleteAddress(_adapterIndex, ipAddress);
    }

    [Obsolete("Migration (2026-10): drop with MigrateSavedAddresses, its only caller.")]
    private IPAddress[] GetAdapterAddresses(AddressFamily addressFamily)
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .First(x => x.Name == AdapterName)
            .GetIPProperties().UnicastAddresses
            .Select(x => x.Address)
            .Where(x => x.AddressFamily == addressFamily && !x.IsIPv6LinkLocal)
            .ToArray();
    }

    // ReSharper disable once UnusedMember.Local
    private async Task AddRouteUsingNetsh(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"interface ipv4 add route {ipNetwork} \"{AdapterName}\""
            : $"interface ipv6 add route {ipNetwork} \"{AdapterName}\"";

        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken).Vhc();
    }

    protected override Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        if (_adapterIndex == 0)
            throw new InvalidOperationException("Adapter index is not set. Call AdapterOpen() first.");

        VhLogger.Instance.LogTrace("Adding route {IpNetwork} to {AdapterName}.", ipNetwork, AdapterName);
        Win32IpHelper.AddRoute(_adapterIndex, ipNetwork, cancellationToken);
        return Task.CompletedTask;
    }

    // ReSharper disable once UnusedMember.Local
    private static uint GetAdapterIndex(string adapterName)
    {
        var networkInterface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x => x.Name == adapterName);
        if (networkInterface == null)
            throw new InvalidOperationException($"Could not find network adapter with name '{adapterName}'.");

        // Get the index of the adapter
        if (networkInterface.Supports(NetworkInterfaceComponent.IPv4)) {
            var index = (uint)networkInterface.GetIPProperties().GetIPv4Properties().Index;
            if (index != 0)
                return index; // Return the index if it is valid (not 0)
        }

        if (networkInterface.Supports(NetworkInterfaceComponent.IPv6)) {
            var index = (uint)networkInterface.GetIPProperties().GetIPv6Properties().Index;
            if (index != 0)
                return index; // Return the index if it is valid (not 0)
        }

        // If neither IPv4 nor IPv6 is supported, throw an exception
        throw new InvalidOperationException($"Adapter '{adapterName}' does not support IPv4 or IPv6.");
    }

    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        if (ipV4)
            Win32IpHelper.SetMtu(_adapterIndex, AddressFamily.InterNetwork, mtu);

        if (ipV6)
            Win32IpHelper.SetMtu(_adapterIndex, AddressFamily.InterNetworkV6, mtu);

        return Task.CompletedTask;
    }

    // A failure fails the start: without the tunnel's DNS, Windows asks the other adapters' servers, often a
    // router outside the tunnel, and the user would never know
    protected override async Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers,
        CancellationToken cancellationToken)
    {
        foreach (var addressFamily in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 }) {
            var servers = dnsServers.Where(ip => ip.AddressFamily == addressFamily).ToArray();
            if (servers.Length == 0)
                continue;

            // the API where Windows has it; PowerShell is a process per IP version, ~0.75 s each
            if (Win32IpHelper.IsInterfaceDnsSettingsSupported)
                Win32IpHelper.SetDnsServers(_adapterIndex, addressFamily, servers);
            else
                await ExecutePowerShellCommandAsync(
                    $"Set-DnsClientServerAddress -InterfaceAlias '{AdapterName}' " +
                    $"-ServerAddresses @({string.Join(",", servers.Select(ip => $"'{ip}'"))})",
                    cancellationToken).Vhc();
        }
    }

    private string NatName => $"{AdapterName}Nat";
    private static readonly TimeSpan NatAddTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NatRemoveTimeout = TimeSpan.FromSeconds(15);

    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // WinNAT keeps a NAT after its process, so one a killed run left goes first; a failure here
        // shows in New-NetNat's own
        VhInvoker.TryInvoke("remove the NAT a previous run left", RemoveNat);

        // Never cut off by a stop once begun, only by its own time limit: a NAT the command made after
        // the start's cleanup looked would stay.
        cancellationToken.ThrowIfCancellationRequested();
        using var timeoutCts = new CancellationTokenSource(NatAddTimeout);
        await ExecutePowerShellCommandAsync(
            $"New-NetNat -Name '{NatName}' -InternalIPInterfaceAddressPrefix '{ipNetwork}'",
            timeoutCts.Token).Vhc();
    }

    // by the name alone: a NAT of this adapter's name is its own, whatever network a run gave it
    private void RemoveNat()
    {
        ExecutePowerShellCommand(
            $"Get-NetNat | Where-Object {{ $_.Name -eq '{NatName}' }} | Remove-NetNat -Confirm:$false",
            NatRemoveTimeout);
    }

    private static Task<string> ExecutePowerShellCommandAsync(string command, CancellationToken cancellationToken)
    {
        var ps = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"";
        return OsUtils.ExecuteCommandAsync("powershell.exe", ps, cancellationToken);
    }

    private static string ExecutePowerShellCommand(string command, TimeSpan timeout)
    {
        var ps = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"";
        return OsUtils.ExecuteCommand("powershell.exe", ps, timeout);
    }

    protected override void WaitForTunRead()
    {
        var readEvent = _readEvent;
        if (readEvent == IntPtr.Zero)
            throw new IOException("WinTun session is closed.");

        var result = Kernel32.WaitForSingleObject(readEvent, Kernel32.Infinite);
        if (result == Kernel32.WaitObject0)
            return;

        throw result == Kernel32.WaitFailed
            ? new Win32Exception()
            : new PInvokeException("Unexpected result from WaitForSingleObject", (int)result);
    }


    protected override bool ReadPacket(byte[] buffer)
    {
        var session = _tunSession;
        if (session == IntPtr.Zero)
            throw new IOException("WinTun session is closed.");

        var tunReceivePacket = WinTunApi.WintunReceivePacket(session, out var size);

        // return if something is written
        if (tunReceivePacket != IntPtr.Zero) {
            try {
                Marshal.Copy(tunReceivePacket, buffer, 0, size);
                return true;
            }
            finally {
                WinTunApi.WintunReleaseReceivePacket(session, tunReceivePacket);
            }
        }

        // check for errors
        var lastError = (WintunReceivePacketError)Marshal.GetLastWin32Error();
        return lastError switch {
            WintunReceivePacketError.NoMoreItems => false,
            WintunReceivePacketError.HandleEof => throw new IOException("WinTun adapter has been closed."),
            WintunReceivePacketError.InvalidData => throw new InvalidOperationException(
                "Invalid data received from WinTun adapter."),
            _ => throw new PInvokeException(
                $"Unknown error in reading packet from WinTun. LastError: {lastError}")
        };
    }

    protected override void WaitForTunWrite()
    {
        Thread.Sleep(1);
    }

    // A real call, never inlined: the batch's sending flag, set before it, then stays ahead of this read of
    // the session, which a stop's wait for the sender counts on (WaitForSender)
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected override bool WritePacket(IpPacket ipPacket)
    {
        var session = _tunSession;
        if (session == IntPtr.Zero)
            return false;

        var packetBytes = ipPacket.Buffer;

        // Allocate memory for the packet inside WinTun ring buffer
        var packetMemory = WinTunApi.WintunAllocateSendPacket(session, packetBytes.Length); // thread-safe
        if (packetMemory == IntPtr.Zero)
            return false;

        // Copy the raw packet data into WinTun memory
        var buffer = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var offset, out var length);
        Marshal.Copy(buffer, offset, packetMemory, length);

        // Send the packet through WinTun
        WinTunApi.WintunSendPacket(session, packetMemory); // thread-safe
        return true;
    }

    // The embedded wintun.dll, installed into a protected folder of its own under Common Files, not the
    // temp folder, which on some Windows ordinary users write and SYSTEM loads from. Every WinTun import
    // binds to this one module, never to a DLL of that name a search or another load may find; its
    // dependencies come from System32 and its own folder.
    private static void LoadWinTunDll()
    {
        lock (WinTunDllLock) {
            if (_winTunDll != IntPtr.Zero)
                return;

            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("WinTun runs on Windows only.");

            // the DLL matches the process, which may be x64 on ARM64 Windows; WinTun installs its own driver
            var architecture = RuntimeInformation.ProcessArchitecture switch {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new NotSupportedException("WinTun runs in an x64 or arm64 process only.")
            };

            using var dllResource = OpenResource($"wintun-{architecture}.dll");
            var folderPath = WindowsCommonFiles.Install($"WinTun-{architecture}", new Dictionary<string, Stream> {
                ["wintun.dll"] = dllResource
            });

            var assembly = typeof(WinTunVpnAdapter).Assembly;
            var winTunDll = NativeLibrary.Load(Path.Combine(folderPath, "wintun.dll"), assembly,
                DllImportSearchPath.System32 | DllImportSearchPath.UseDllDirectoryForDependencies);
            NativeLibrary.SetDllImportResolver(assembly,
                (libraryName, _, _) => libraryName == "wintun.dll" ? winTunDll : IntPtr.Zero);
            _winTunDll = winTunDll;
        }
    }

    private static Stream OpenResource(string name)
    {
        return typeof(WinTunVpnAdapter).Assembly.GetManifestResourceStream(name) ??
               throw new InvalidOperationException($"The embedded {name} is missing.");
    }

    protected override void DisposeUnmanaged()
    {
        // The adapter is an unmanaged resource; it must be closed if it is open. AdapterRemove also
        // removes the NAT rules — on a normal Dispose that already happened via Stop(), but the finalizer
        // path never runs Stop(), so we must clean NAT here too. A leftover NAT rule pointing at a dead
        // interface can break the host network, so cleaning it (even on the finalizer thread) is the
        // lesser evil versus leaking it.
        if (_tunAdapter != IntPtr.Zero)
            AdapterRemove();

        base.DisposeUnmanaged();
    }

    ~WinTunVpnAdapter()
    {
        Dispose(false);
    }
}
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

    // _tunAdapter is a raw native pointer freed by WintunCloseAdapter; passing it to
    // WintunStartSession after (or while) it is freed corrupts the process. This lock makes
    // AdapterOpen atomic against AdapterRemove, which can run concurrently when the user
    // disconnects while a connection is still setting up the adapter.
    private readonly Lock _adapterLock = new();

    public const int MinRingCapacity = 0x20000; // 128kiB
    public const int MaxRingCapacity = 0x4000000; // 64MiB
    protected override bool IsSocketProtectedByBind => true;
    // WinNAT translates IPv4 only: New-NetNat answers "IPV6 is not supported."
    public override bool IsNatSupported(IpVersion ipVersion) => ipVersion == IpVersion.IPv4;
    public override bool IsAppFilterSupported => false;
    protected override string? AppPackageId => null;
    protected override bool RestartAfterNetworkAddressChanged => true;

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

        // start wintun logger
        // create the adapter
        _tunAdapter = WinTunApi.WintunCreateAdapter(AdapterName, "VPN", BuildGuidFromName(AdapterName));

        // close the adapter if it was already exists error 183 (ERROR_ALREADY_EXISTS)
        var lastErrorCode = Marshal.GetLastWin32Error();
        if (_tunAdapter == IntPtr.Zero && lastErrorCode == 183) {
            var prevAdapter = WinTunApi.WintunOpenAdapter(AdapterName);
            if (prevAdapter != IntPtr.Zero)
                WinTunApi.WintunCloseAdapter(prevAdapter);

            // try to create the adapter again, then refresh the error so a failure here is not
            // reported with the stale ERROR_ALREADY_EXISTS from the first attempt.
            _tunAdapter = WinTunApi.WintunCreateAdapter(AdapterName, "VPN", BuildGuidFromName(AdapterName));
            lastErrorCode = Marshal.GetLastWin32Error();
        }

        if (_tunAdapter == IntPtr.Zero)
            throw new PInvokeException(
                $"Failed to create WinTun adapter. Make sure the app is running with admin privilege. ErrorCode: {lastErrorCode}",
                lastErrorCode);

        _adapterIndex = GetAdapterIndex(AdapterName);
        return Task.CompletedTask;
    }

    protected override void AdapterRemove()
    {
        lock (_adapterLock) {
            // WinTun requires ending sessions before closing the adapter. A live session may still
            // exist here when AdapterOpen raced a concurrent Stop and won, so end it here rather
            // than relying on callers to close before remove.
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
        // Zero the fields first so concurrent readers/writers see the session as closed,
        // then end the session to signal blocked WintunReceivePacket calls.
        var session = Interlocked.Exchange(ref _tunSession, IntPtr.Zero);
        _readEvent = IntPtr.Zero;

        if (session != IntPtr.Zero)
            WinTunApi.WintunEndSession(session);
    }

    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        // not supported. ignore
        return Task.CompletedTask;
    }

    protected override async Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        if (ipV4)
            await OsUtils.ExecuteCommandAsync("netsh",
                $"interface ipv4 set interface \"{AdapterName}\" metric={metric}", cancellationToken);

        if (ipV6)
            await OsUtils.ExecuteCommandAsync("netsh",
                $"interface ipv6 set interface \"{AdapterName}\" metric={metric}", cancellationToken);
    }

    protected override async Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"interface ipv4 set address \"{AdapterName}\" static {ipNetwork}"
            : $"interface ipv6 set address \"{AdapterName}\" {ipNetwork}";

        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
    }

    // ReSharper disable once UnusedMember.Local
    private async Task AddRouteUsingNetsh(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"interface ipv4 add route {ipNetwork} \"{AdapterName}\""
            : $"interface ipv6 add route {ipNetwork} \"{AdapterName}\"";

        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
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

    protected override async Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        if (ipV4)
            await OsUtils.ExecuteCommandAsync("netsh", $"interface ipv4 set subinterface \"{AdapterName}\" mtu={mtu}",
                cancellationToken);

        if (ipV6)
            await OsUtils.ExecuteCommandAsync("netsh", $"interface ipv6 set subinterface \"{AdapterName}\" mtu={mtu}",
                cancellationToken);
    }


    //protected override async Task RemoveAllDnsServers(CancellationToken cancellationToken)
    //{
    //    var commandV4 = $"interface ipv4 set dns \"{AdapterName}\" dhcp";
    //    var commandV6 = $"interface ipv6 set dns \"{AdapterName}\" dhcp";

    //    await OsUtils.ExecuteCommandAsync("netsh", commandV4, cancellationToken);
    //    await OsUtils.ExecuteCommandAsync("netsh", commandV6, cancellationToken);
    //}

    /*
    protected override async Task SetDnsServers(IEnumerable<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        // remove previous DNS servers.
        // Do not log in debug mode because it is common error as the adapter is usually new
        VhLogger.Instance.LogDebug("Removing previous DNS from the adapter...");
        await VhUtils.TryInvokeAsync(VhLogger.MinLogLevel == LogLevel.Trace ? "Remove previous IPv4 DNS" : "",
            () => OsUtils.ExecuteCommandAsync("netsh", $"netsh interface ipv4 delete dns \"{AdapterName}\" all",
                cancellationToken));

        await VhUtils.TryInvokeAsync(VhLogger.MinLogLevel == LogLevel.Trace ? "Remove previous IPv6 DNS" : "",
            () => OsUtils.ExecuteCommandAsync("netsh", $"netsh interface ipv6 delete dns \"{AdapterName}\" all",
                cancellationToken));

        VhLogger.Instance.LogDebug("Adding new DNS to the adapter...");
        foreach (var ipAddress in dnsServers) {
            var command = ipAddress.IsV4()
                ? $"interface ipv4 add dns \"{AdapterName}\" {ipAddress}"
                : $"interface ipv6 add dns \"{AdapterName}\" {ipAddress}";

            await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
        }
    }
    */

    protected override async Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers,
        CancellationToken cancellationToken)
    {
        var ipv4 = dnsServers
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            .ToArray();

        var ipv6 = dnsServers
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6)
            .ToArray();

        VhLogger.Instance.LogDebug("Setting DNS via PowerShell...");

        if (ipv4.Length > 0) {
            var v4List = string.Join(",", ipv4.Select(ip => $"'{ip}'"));
            var cmd = $"Set-DnsClientServerAddress -InterfaceAlias '{AdapterName}' -ServerAddresses @({v4List})";

            await VhUtils.TryInvokeAsync(
                VhLogger.MinLogLevel == LogLevel.Trace ? "Set IPv4 DNS" : "",
                () => OsUtils.ExecuteCommandAsync(
                    "powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"",
                    cancellationToken));
        }

        if (ipv6.Length > 0) {
            var v6List = string.Join(",", ipv6.Select(ip => $"'{ip}'"));
            var cmd = $"Set-DnsClientServerAddress -InterfaceAlias '{AdapterName}' -ServerAddresses @({v6List})";

            await VhUtils.TryInvokeAsync(
                VhLogger.MinLogLevel == LogLevel.Trace ? "Set IPv6 DNS" : "",
                () => OsUtils.ExecuteCommandAsync(
                    "powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"",
                    cancellationToken));
        }
    }

    private string NatName => $"{AdapterName}Nat";
    private static readonly TimeSpan NatAddTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NatRemoveTimeout = TimeSpan.FromSeconds(15);

    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // WinNAT keeps a NAT after its process, so one a killed run left goes first; a failure here
        // shows in New-NetNat's own
        VhUtils.TryInvoke("remove the NAT a previous run left", RemoveNat);

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
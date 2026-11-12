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

/// <summary>
/// Provides the VPN adapter for Windows that runs on WinTun, a layer-3 TUN driver: it makes the interface and gives it
/// its addresses, routes, DNS servers, MTU and metric, and, for a server, IPv4 NAT through WinNAT.
/// </summary>
/// <param name="adapterSettings">The adapter's settings.</param>
/// <remarks>
/// <para>
/// It runs as an administrator only. The first start in a process installs the wintun.dll it carries, for an x64 or
/// arm64 process, into a protected folder of its own under Common Files, or takes the one installed there, and loads
/// it for the process's life; WinTun installs its driver with the first interface. The interface's GUID comes from
/// its name (<see cref="BuildGuidFromName"/>).
/// </para>
/// <para>
/// Windows asks every adapter's DNS servers at once, whatever the routes, so a start that sets DNS servers also adds a
/// Name Resolution Policy Table (NRPT) rule that makes the tunnel's servers the only ones; Group Policy's own NRPT
/// rules override it, which the adapter logs. It has no app filter, and its NAT translates IPv4 only.
/// </para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">
/// The settings' blocking timeout is negative or too long, or their queue capacity is not above zero.
/// </exception>
public class WinTunVpnAdapter(WinVpnAdapterSettings adapterSettings)
    : TunVpnAdapter(adapterSettings)
{
    private uint _adapterIndex;
    private readonly Guid _adapterGuid = BuildGuidFromName(adapterSettings.AdapterName);
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

    /// <summary>The smallest ring size WinTun takes, 128 KiB.</summary>
    public const int MinRingCapacity = 0x20000;

    /// <summary>The largest ring size WinTun takes, 64 MiB.</summary>
    public const int MaxRingCapacity = 0x4000000;

    /// <inheritdoc/>
    /// <value>Always <see langword="true"/>.</value>
    protected override bool IsSocketProtectedByBind => true;

    /// <inheritdoc/>
    /// <remarks>WinNAT translates IPv4 only: <c>New-NetNat</c> answers "IPV6 is not supported."</remarks>
    public override bool IsNatSupported(IpVersion ipVersion) => ipVersion == IpVersion.IPv4;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    public override bool IsAppFilterSupported => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: there is no app filter.</value>
    protected override string? AppPackageId => null;

    /// <inheritdoc/>
    /// <value>
    /// <see langword="true"/> when the adapter uses NAT, as a server's does, which adds its NAT only with a primary
    /// address; <see langword="false"/> for a client's, which takes a new network without a restart, whose teardown
    /// would let the host's traffic pass outside the tunnel for its seconds.
    /// </value>
    protected override bool RestartAfterNetworkAddressChanged => UseNat;

    /// <inheritdoc/>
    /// <value>Always <see langword="true"/>.</value>
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => true;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: WinTun has no app filter.</exception>
    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on WinTun.");

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: WinTun has no app filter.</exception>
    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on WinTun.");

    /// <summary>
    /// Builds the GUID of an adapter name's interface: the same for the name at every start, so a kill switch finds
    /// the interface by it.
    /// </summary>
    /// <param name="adapterName">The adapter's name, <see cref="VpnAdapterSettings.AdapterName"/>.</param>
    /// <returns>A GUID in the version 5 layout, made from the SHA-1 hash of <c>VpnHood.</c> and the name.</returns>
    public static Guid BuildGuidFromName(string adapterName)
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

    /// <inheritdoc/>
    /// <remarks>
    /// The first start loads wintun.dll, installing it first. An interface of the name whose process ended a moment
    /// ago may still be on its way out; the create is then tried once more.
    /// </remarks>
    /// <exception cref="PlatformNotSupportedException">The OS is not Windows.</exception>
    /// <exception cref="NotSupportedException">The process is neither x64 nor arm64.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The process is not an administrator's, or Common Files or the install folder is a link or one others than
    /// administrators may change, so wintun.dll cannot be installed.
    /// </exception>
    /// <exception cref="PInvokeException">
    /// WinTun could not create the interface, as when another process holds an interface of the name.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The package's wintun.dll is missing; the installed one is not the one the package carries, is a link, or is one
    /// others than administrators may change; or the new interface was not found by its name or has no IPv4 or IPv6
    /// index.
    /// </exception>
    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        // Load the WinTun DLL
        LoadWinTunDll();

        // create the adapter. One of this name whose process ended a moment ago (a crash, a debugger's stop)
        // is still being removed: a create racing that removal gives up after WinTun's 15 s wait with the
        // plain code, by when that device is gone, so it is created once more
        _tunAdapter = WinTunApi.WintunCreateAdapter(AdapterName, "VPN", _adapterGuid);
        var errorCode = _tunAdapter == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        if (errorCode == ErrorAlreadyExists) {
            VhLogger.Instance.LogWarning(
                "The WinTun adapter {AdapterName} of an ended process was still being removed. Creating it again...",
                AdapterName);
            _tunAdapter = WinTunApi.WintunCreateAdapter(AdapterName, "VPN", _adapterGuid);
            errorCode = _tunAdapter == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        }

        // A live one is not ours to remove: WinTun removes an adapter only by the handle that created it.
        // Error 2 failed every create once, in the boot that first installed WinTun's driver (Windows 10,
        // CR_INVALID_DEVNODE in setupapi.dev.log; older builds too); a reboot cleared it, not seen since.
        // No hint to run as an administrator: LoadWinTunDll lets no other process this far
        if (_tunAdapter == IntPtr.Zero)
            throw errorCode is ErrorAlreadyExists or ErrorAlreadyExistsHResult
                ? new PInvokeException(
                    $"The WinTun adapter {AdapterName} already exists: another process holds it. ErrorCode: {errorCode}",
                    errorCode)
                : new PInvokeException(
                    $"Failed to create the WinTun adapter. {new Win32Exception(errorCode).Message} ErrorCode: {errorCode}",
                    errorCode);

        _adapterIndex = GetAdapterIndex(AdapterName);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It also removes the interface's NRPT rule and, with NAT, the NAT; a failure of either is logged, not thrown. A
    /// later start tries to replace or remove the NRPT rule, and one that adds NAT tries to remove a NAT left behind.
    /// </remarks>
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

        // the DNS client would keep asking the tunnel's servers alone, now around it
        try {
            NrptRule.Remove(_adapterGuid);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "Could not remove the DNS rule of the {AdapterName} TUN adapter; the next start or boot removes it.",
                AdapterName);
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

    /// <inheritdoc/>
    /// <remarks>It starts a WinTun session with rings of <see cref="WinVpnAdapterSettings.RingCapacity"/>.</remarks>
    /// <exception cref="InvalidOperationException">A stop removed the interface while the start was under way.</exception>
    /// <exception cref="Win32Exception">
    /// WinTun could not start the session, as for a ring size it does not take.
    /// </exception>
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

    /// <inheritdoc/>
    /// <remarks>
    /// Ending the session frees its rings, so it first wakes the reader and waits up to a second for it to leave the
    /// session, and up to a second plus twice <see cref="VpnAdapterSettings.MaxPacketSendDelay"/> for a batch being
    /// sent, then 100 ms more for each that is late; a reader or sender that stops the adapter itself is not waited
    /// for. When one does not leave in time, the session stays allocated until the process exits, which is logged as
    /// an error.
    /// </remarks>
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

    /// <inheritdoc/>
    /// <remarks>It does nothing: WinTun shows no session name.</remarks>
    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        // not supported. ignore
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>The interface's metric then stops being automatic.</remarks>
    /// <exception cref="Win32Exception">Windows refused the change.</exception>
    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        if (ipV4)
            Win32IpHelper.SetMetric(_adapterIndex, AddressFamily.InterNetwork, metric);

        if (ipV6)
            Win32IpHelper.SetMetric(_adapterIndex, AddressFamily.InterNetworkV6, metric);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It turns off duplicate address detection on the interface first: a tunnel shares its link with no one, and the
    /// detection would keep the address unusable for seconds after the interface comes up. The address lasts as long
    /// as the interface.
    /// </remarks>
    /// <exception cref="Win32Exception">Windows refused the change.</exception>
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

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The interface has not been made yet.</exception>
    /// <exception cref="Win32Exception">Windows refused the route.</exception>
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

    /// <inheritdoc/>
    /// <exception cref="Win32Exception">Windows refused the change.</exception>
    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        if (ipV4)
            Win32IpHelper.SetMtu(_adapterIndex, AddressFamily.InterNetwork, mtu);

        if (ipV6)
            Win32IpHelper.SetMtu(_adapterIndex, AddressFamily.InterNetworkV6, mtu);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// For each IP version that has servers, they replace the interface's own, through the IP Helper API from Windows 10
    /// 2004, through PowerShell before it. Then an NRPT rule makes them the only servers Windows asks, unless Group
    /// Policy sets NRPT rules of its own, which is logged: Windows asks every adapter's servers at once, each through
    /// its own adapter whatever the routes, often a router outside the tunnel. With no servers, the rule is removed,
    /// and the interface's settings are left as they are.
    /// </remarks>
    /// <exception cref="Win32Exception">Windows refused the servers, or PowerShell could not be started.</exception>
    /// <exception cref="ExternalException">PowerShell's command failed, before Windows 10 2004.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled before or while the PowerShell command ran.
    /// </exception>
    // A failure fails the start, as the user would never know the DNS went around the tunnel
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

        // a server's adapter has none; a rule a crash left goes
        if (dnsServers.Count == 0) {
            NrptRule.Remove(_adapterGuid);
            return;
        }

        if (NrptRule.HasGroupPolicyRules)
            VhLogger.Instance.LogWarning(
                "Group Policy sets DNS rules (NRPT) on this device, so Windows ignores the VPN's, and asks the other " +
                "adapters' DNS servers too, outside the VPN.");

        NrptRule.Set(_adapterGuid, dnsServers, AdapterName);
    }

    private string NatName => $"{AdapterName}Nat";
    private static readonly TimeSpan NatAddTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NatRemoveTimeout = TimeSpan.FromSeconds(15);

    /// <inheritdoc/>
    /// <remarks>
    /// It runs PowerShell's <c>New-NetNat</c>, after trying to remove a NAT of the adapter's name that a killed run
    /// left, as WinNAT keeps one after its process. A stop does not cut it off once begun, so a failed start's cleanup
    /// comes after the command ends; after 60 seconds it stops waiting for PowerShell, which may go on and still make
    /// the NAT.
    /// </remarks>
    /// <exception cref="ExternalException"><c>New-NetNat</c> failed, as WinNAT does for an IPv6 network.</exception>
    /// <exception cref="Win32Exception">PowerShell could not be started.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled before the command began, or the command ran 60 seconds.
    /// </exception>
    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // WinNAT keeps a NAT after its process, so one a killed run left goes first; a failure here
        // shows in New-NetNat's own
        VhInvoker.TryInvoke("remove the NAT a previous run left", LogLevel.Debug, RemoveNat);

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

    private static void ExecutePowerShellCommand(string command, TimeSpan timeout)
    {
        var ps = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"";
        OsUtils.ExecuteCommand("powershell.exe", ps, timeout);
    }

    /// <inheritdoc/>
    /// <exception cref="IOException">The session is closed.</exception>
    /// <exception cref="Win32Exception">The wait for WinTun's read event failed.</exception>
    /// <exception cref="PInvokeException">The wait for WinTun's read event ended in another way.</exception>
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


    /// <inheritdoc/>
    /// <exception cref="IOException">The session or the interface is closed.</exception>
    /// <exception cref="InvalidOperationException">WinTun found its ring's data invalid.</exception>
    /// <exception cref="PInvokeException">WinTun failed with another error.</exception>
    protected override int ReadPacket(byte[] buffer)
    {
        var session = _tunSession;
        if (session == IntPtr.Zero)
            throw new IOException("WinTun session is closed.");

        var tunReceivePacket = WinTunApi.WintunReceivePacket(session, out var size);

        // return if something is written
        if (tunReceivePacket != IntPtr.Zero) {
            try {
                Marshal.Copy(tunReceivePacket, buffer, 0, size);
                return size;
            }
            finally {
                WinTunApi.WintunReleaseReceivePacket(session, tunReceivePacket);
            }
        }

        // check for errors
        var lastError = (WintunReceivePacketError)Marshal.GetLastWin32Error();
        return lastError switch {
            WintunReceivePacketError.NoMoreItems => 0,
            WintunReceivePacketError.HandleEof => throw new IOException("WinTun adapter has been closed."),
            WintunReceivePacketError.InvalidData => throw new InvalidOperationException(
                "Invalid data received from WinTun adapter."),
            _ => throw new PInvokeException(
                $"Unknown error in reading packet from WinTun. LastError: {lastError}")
        };
    }

    /// <inheritdoc/>
    /// <remarks>It sleeps a millisecond: WinTun has no event for room in its ring.</remarks>
    protected override void WaitForTunWrite()
    {
        Thread.Sleep(1);
    }

    // A real call, never inlined: the batch's sending flag, set before it, then stays ahead of this read of
    // the session, which a stop's wait for the sender counts on (WaitForSender)
    /// <inheritdoc/>
    /// <returns>
    /// <see langword="true"/> if WinTun took the packet; <see langword="false"/> if it could not allocate the packet in
    /// its ring, as when the ring is full, or if the session is closed.
    /// </returns>
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
            WinTunLogger.Register();
            _winTunDll = winTunDll;
        }
    }

    private static Stream OpenResource(string name)
    {
        return typeof(WinTunVpnAdapter).Assembly.GetManifestResourceStream(name) ??
               throw new InvalidOperationException($"The embedded {name} is missing.");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An interface still there is removed, and the removal of its NRPT rule and NAT tried, also when the finalizer calls
    /// it, which no stop came before: a NAT left pointing at a dead interface can break the host's network.
    /// </remarks>
    protected override void DisposeUnmanaged()
    {
        if (_tunAdapter != IntPtr.Zero)
            AdapterRemove();

        base.DisposeUnmanaged();
    }

    /// <summary>Removes the interface of an adapter that was not disposed, as <see cref="DisposeUnmanaged"/> does.</summary>
    /// <remarks>
    /// The adapter's handlers on the host's network-change events keep it reachable until it is disposed, so dispose it
    /// rather than count on this.
    /// </remarks>
    ~WinTunVpnAdapter()
    {
        Dispose(false);
    }
}
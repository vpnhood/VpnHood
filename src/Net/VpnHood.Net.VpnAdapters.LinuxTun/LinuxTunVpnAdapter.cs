using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.LinuxTun.LinuxNative;

namespace VpnHood.Net.VpnAdapters.LinuxTun;

public class LinuxTunVpnAdapter : TunVpnAdapter
{
    // -1 (not 0) is the "closed" sentinel: fd 0 is a valid descriptor (stdin), so using it would let a
    // post-close read/write hit an unrelated descriptor instead of failing.
    private const int InvalidFd = -1;

    // IFNAMSIZ less its terminator; the kernel and ip(8) refuse longer ones
    private const int MaxAdapterNameLength = 15;
    private const int KeptNameLength = 10;

    // IFALIASZ less its terminator
    private const int MaxAdapterAliasLength = 255;
    private const int KeptAliasLength = 64;

    private readonly string? _adapterAlias;
    private int _tunAdapterFd = InvalidFd;
    private int? _metric;
    private string? _primaryAdapterName;
    private bool _isAdapterAdded;
    private bool _isResolvconfDnsSet;
    private StructPollfd[]? _pollFdReads;
    private StructPollfd[]? _pollFdWrites;
    private readonly byte[] _writeBuffer = new byte[0xFFFF];
    protected override bool IsSocketProtectedByBind => true;
    public override bool CanProtectSocket => !string.IsNullOrEmpty(_primaryAdapterName);

    public override bool IsNatSupported => true;
    public override bool IsAppFilterSupported => false;
    protected override string? AppPackageId => null;
    protected override bool RestartAfterNetworkAddressChanged => true;

    public LinuxTunVpnAdapter(LinuxVpnAdapterSettings adapterSettings)
        : base(adapterSettings)
    {
        if (!IsValidAdapterName(AdapterName))
            throw new ArgumentException(
                $"'{AdapterName}' is not a valid Linux interface name; {nameof(GetValidAdapterName)} derives one.",
                nameof(adapterSettings));

        _adapterAlias = adapterSettings.AppId == null ? null : GetAdapterAlias(adapterSettings.AppId);
    }

    // A name the kernel takes: at most 15 characters, and none it refuses - whitespace, '/' or ':'.
    // This keeps to letters, digits, '_', '-' and '.', and never starts with '-', which a command
    // line would read as an option.
    public static bool IsValidAdapterName(string name)
    {
        return name.Length is > 0 and <= MaxAdapterNameLength &&
               name is not ("." or "..") &&
               name[0] != '-' &&
               name.All(IsValidAdapterNameChar);
    }

    // A valid name passes unchanged (VpnHoodClient, VpnHoodConnect, VpnHoodServer all are). Any other
    // becomes its first ten valid characters, a dash and four hex digits of a hash of the whole name,
    // so two long names that share a prefix still differ; the app id, not the name, decides whose
    // a tun is.
    public static string GetValidAdapterName(string name)
    {
        if (IsValidAdapterName(name))
            return name;

        var kept = new string(name.Where(IsValidAdapterNameChar).Take(KeptNameLength).ToArray())
            .TrimStart('-', '.');
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..4];
        return kept.Length > 0 ? $"{kept}-{hash}" : $"vh-{hash}";
    }

    private static bool IsValidAdapterNameChar(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.';
    }

    // An app id is any string. One the kernel takes as an alias, in characters a command line needs
    // no quoting for, passes unchanged, so today's aliases stay; any other becomes its first 64
    // characters, those an alias cannot hold as '_', a dash and 16 hex digits of a hash of the
    // whole id, which keeps two ids apart however much they share.
    internal static string GetAdapterAlias(string appId)
    {
        if (appId.Length is > 0 and <= MaxAdapterAliasLength && appId.All(IsValidAliasChar))
            return appId;

        var kept = new string(appId.Take(KeptAliasLength)
            .Select(c => IsValidAliasChar(c) ? c : '_').ToArray());
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(appId)))[..16];
        return kept.Length > 0 ? $"{kept}-{hash}" : $"app-{hash}";
    }

    private static bool IsValidAliasChar(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '/';
    }

    // Whether a tun is one this app left and may clear: nobody holds it, and it carries the app's id,
    // or, from a version before tags, no tag and this adapter's name.
    internal static bool IsOwnLeftover(LinuxTunInfo tun, string adapterName, string? adapterAlias)
    {
        if (tun.IsHeld)
            return false;

        // Migration (2026-09), the untagged case - versions before tags: drop a few months after it ships.
        return tun.Alias.Length == 0
            ? tun.Name == adapterName
            : tun.Alias == adapterAlias;
    }

    // At the daemon's start, after its single-instance lock, rather than at the next connect: a crash
    // leaves its tun with the routes on. Every tun that carries this app's id and no process holds
    // goes, its DNS entry first; nothing here throws. An untagged one, known only by the adapter's
    // name, the adapter's own start clears.
    public static void RemoveLeftovers(string appId)
    {
        var adapterAlias = GetAdapterAlias(appId);
        foreach (var tun in TryListTuns().Where(x => x.Alias == adapterAlias)) {
            if (tun.IsHeld) {
                VhLogger.Instance.LogWarning("Leaving {AdapterName} alone: a process holds it.", tun.Name);
                continue;
            }

            VhLogger.Instance.LogInformation("Removing {AdapterName}, a tun a previous run left.", tun.Name);
            TryRemoveResolvconfDns(tun.Name);
            VhUtils.TryInvoke($"remove the leftover tun {tun.Name}", () =>
                ExecuteCommand($"ip link delete {tun.Name}"));
        }
    }

    private static IReadOnlyList<LinuxTunInfo> TryListTuns()
    {
        try {
            return LinuxTunInfo.List();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not list the tun devices, so no leftover was removed.");
            return [];
        }
    }

    // Why a tun cannot be taken for this adapter, in the words its start reports.
    private static string DescribeUnusable(LinuxTunInfo tun)
    {
        var owner = tun.Alias.Length > 0 ? tun.Alias : "an app that does not tag its tun";
        return tun.IsHeld
            ? $"the interface {tun.Name} is in use by another VPN ({owner}), or is down. " +
              "Stop that VPN, or give this app another name."
            : $"the interface {tun.Name} belongs to {owner}. " +
              $"If that app is gone, remove it with: sudo ip link delete {tun.Name}";
    }

    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    private static async Task<string> GetPrimaryAdapterName(CancellationToken cancellationToken)
    {
        var mainInterface = await ExecuteCommandAsync("ip route | grep default | awk '{print $5}'", cancellationToken).Vhc();
        mainInterface = mainInterface.Split("\n").FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(mainInterface))
            throw new InvalidOperationException("No active network interface found.");

        return mainInterface;
    }

    public static string? FindInterfaceNameForIp(IPAddress ip)
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var networkInterface in interfaces) {
            var props = networkInterface.GetIPProperties();
            var addresses = props.UnicastAddresses;

            if (addresses.Any(ua => ua.Address.Equals(ip))) {
                return networkInterface.Name; // on Linux, this will be "eth0", "tun0", etc.
            }
        }

        return null; // not found
    }

    protected override async Task AdapterAdd(CancellationToken cancellationToken)
    {
        // Get the primary adapter name
        VhLogger.Instance.LogDebug("Getting the primary adapter name...");
        _primaryAdapterName = await GetPrimaryAdapterName(cancellationToken);
        VhLogger.Instance.LogDebug("Primary adapter name is {PrimaryAdapterName}", _primaryAdapterName);

        // the way clear: this adapter's own previous run, then whatever else has the name
        VhLogger.Instance.LogDebug("Clean previous tun adapter...");
        AdapterRemove();
        ClearAdapterName();

        // Create and configure tun interface
        VhLogger.Instance.LogDebug("Creating tun adapter...");
        await ExecuteCommandAsync($"ip tuntap add dev {AdapterName} mode tun", cancellationToken).Vhc();
        _isAdapterAdded = true;

        // The alias derived from the app id lets a later start tell this tun from another app's.
        if (_adapterAlias != null)
            await ExecuteCommandAsync($"ip link set dev {AdapterName} alias {_adapterAlias}", cancellationToken).Vhc();

        // Enable IP forwarding
        VhLogger.Instance.LogDebug("Enabling IP forwarding...");
        await ExecuteCommandAsync("sysctl -w net.ipv4.ip_forward=1", cancellationToken).Vhc();
        await ExecuteCommandAsync("sysctl -w net.ipv6.conf.all.forwarding=1", cancellationToken).Vhc();

        // Bring up the interface
        VhLogger.Instance.LogDebug("Bringing up the TUN...");
        await ExecuteCommandAsync($"ip link set {AdapterName} up", cancellationToken).Vhc();
    }

    // Only what this adapter added: a start that was refused the name must not take down the tun,
    // or the DNS entry, of the owner that holds it.
    protected override void AdapterRemove()
    {
        // close if open
        AdapterClose();

        if (_isAdapterAdded) {
            // DNS comes off before the interface
            RemoveOwnResolvconfDns();

            if (LinuxTunInfo.InterfaceExists(AdapterName)) {
                VhLogger.Instance.LogDebug("Removing the {AdapterName} TUN adapter...", AdapterName);
                VhUtils.TryInvoke($"remove the {AdapterName} TUN adapter", () =>
                    ExecuteCommand($"ip link delete {AdapterName}"));
            }

            _isAdapterAdded = false;
        }

        // Remove previous NAT iptables record
        if (UseNat) {
            VhLogger.Instance.LogDebug("Removing previous NAT iptables record for {AdapterName} TUN adapter...",
                AdapterName);
            if (AdapterIpNetworkV4 != null)
                TryRemoveNat(AdapterIpNetworkV4);

            if (AdapterIpNetworkV6 != null)
                TryRemoveNat(AdapterIpNetworkV6);
        }
    }

    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        // Open TUN Adapter
        VhLogger.Instance.LogDebug("Opening the TUN adapter...");
        _tunAdapterFd = OpenTunAdapter(AdapterName, false);
        _pollFdReads = [
            new StructPollfd {
                Fd = _tunAdapterFd, Events = OsConstants.Pollin
            }
        ];
        _pollFdWrites = [
            new StructPollfd {
                Fd = _tunAdapterFd, Events = OsConstants.Pollout
            }
        ];

        return Task.CompletedTask;
    }

    protected override void AdapterClose()
    {
        // Close the TUN adapter if it is open
        var fd = Interlocked.Exchange(ref _tunAdapterFd, InvalidFd);
        if (fd != InvalidFd)
            LinuxAPI.close(fd);
    }

    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // remove old NAT rule if any
        TryRemoveNat(ipNetwork);

        // Configure NAT with iptables
        var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";
        await ExecuteCommandAsync(
            $"{iptables} -t nat -A POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE",
            cancellationToken).Vhc();

        // sudo iptables 
        await ExecuteCommandAsync($"{iptables} -A FORWARD -i {AdapterName} -o {_primaryAdapterName} -j ACCEPT",
            cancellationToken).Vhc();

        // sudo iptables 
        await ExecuteCommandAsync(
            $"{iptables} -A FORWARD -i {_primaryAdapterName} -o {AdapterName} -m state --state RELATED,ESTABLISHED -j ACCEPT",
            cancellationToken).Vhc();
    }

    private void TryRemoveNat(IpNetwork ipNetwork)
    {
        var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";

        // Remove NAT rule. try until no rule found
        var res = "ok";
        while (!string.IsNullOrEmpty(res)) {
            res = VhUtils.TryInvoke("Remove NAT rule", () =>
                ExecuteCommand(
                    $"{iptables} -t nat -D POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE"));
        }

        // Remove forwarding rules
        VhUtils.TryInvoke("Remove NAT forwarding rules...", () =>
            ExecuteCommand($"{iptables}-save | grep -v -w \"{AdapterName}\" | {iptables}-restore"));
    }

    protected override async Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        await ExecuteCommandAsync($"ip addr add {ipNetwork} dev {AdapterName}",
            cancellationToken).Vhc();
    }

    protected override async Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"ip route add {ipNetwork} dev {AdapterName}"
            : $"ip -6 route add {ipNetwork} dev {AdapterName}";

        if (_metric != null)
            command += $" metric {_metric}";

        await ExecuteCommandAsync(command, cancellationToken).Vhc();
    }

    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        _metric = metric;
        return Task.CompletedTask;
    }

    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        // Not supported. Ignore
        return Task.CompletedTask;
    }

    protected override async Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        var command = $"ip link set dev {AdapterName} mtu {mtu}";
        await ExecuteCommandAsync(command, cancellationToken).Vhc();
    }

    private async Task SetDnsServersByResolvectl(IEnumerable<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        var allDns = string.Join(" ", dnsServers.Select(x => x.ToString()));
        var command = $"resolvectl dns {AdapterName} {allDns}";
        await ExecuteCommandAsync(command, cancellationToken).Vhc();
        await ExecuteCommandAsync($"resolvectl domain {AdapterName} \"~.\"", cancellationToken).Vhc();
    }

    private async Task SetDnsServersByResolvconf(IEnumerable<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        var dnsPayload = string.Join("\n", dnsServers.Select(x => $"nameserver {x}")) + "\n";
        var command = $"echo \"{dnsPayload}\" | resolvconf -a {AdapterName}";
        await ExecuteCommandAsync(command, cancellationToken).Vhc();
        _isResolvconfDnsSet = true;
    }

    // The resolvconf fallback's entry outlives the interface, where resolvectl's per-link DNS goes
    // with it: left behind, resolv.conf keeps the VPN's nameserver after a disconnect. So it comes
    // off before any interface this owner removes - its own at a disconnect, a leftover at a start -
    // and never for a name another owner holds.
    private static string RemoveResolvconfDnsCommand(string adapterName)
    {
        return $"if command -v resolvconf >/dev/null; then resolvconf -d {adapterName}; fi";
    }

    // An entry this adapter added must come off, so a failure there is a warning.
    private void RemoveOwnResolvconfDns()
    {
        if (!_isResolvconfDnsSet) {
            TryRemoveResolvconfDns(AdapterName);
            return;
        }

        try {
            ExecuteCommand(RemoveResolvconfDnsCommand(AdapterName));
            _isResolvconfDnsSet = false;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "Could not remove the resolvconf DNS entry of {AdapterName}; resolv.conf may keep the VPN's nameserver.",
                AdapterName);
        }
    }

    // Usually there is nothing to remove, and some resolvconf implementations say so as an error.
    private static void TryRemoveResolvconfDns(string adapterName)
    {
        VhUtils.TryInvoke($"remove the resolvconf DNS entry of {adapterName}", () =>
            ExecuteCommand(RemoveResolvconfDnsCommand(adapterName)));
    }

    // Before the tun is created, nothing may hold its name but a leftover of this app, which is
    // cleared. Anything else - a tun in use, another app's, an interface that is not a tun -
    // refuses the start and says whose it is, instead of taking the name over.
    private void ClearAdapterName()
    {
        if (!LinuxTunInfo.InterfaceExists(AdapterName)) {
            TryRemoveResolvconfDns(AdapterName);
            return;
        }

        var tun = LinuxTunInfo.Find(AdapterName) ??
                  throw new InvalidOperationException(
                      $"An interface named {AdapterName} exists and is not a tun, so the VPN cannot create its own.");

        if (!IsOwnLeftover(tun, AdapterName, _adapterAlias))
            throw new InvalidOperationException(
                $"The VPN cannot use its interface name: {DescribeUnusable(tun)}");

        VhLogger.Instance.LogInformation("Removing {AdapterName}, a tun a previous run left.", AdapterName);
        TryRemoveResolvconfDns(AdapterName);
        ExecuteCommand($"ip link delete {AdapterName}");
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    protected override async Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers,
        CancellationToken cancellationToken)
    {
        if (!dnsServers.Any())
            return;

        try {
            // --- Try systemd-resolved ---
            await SetDnsServersByResolvectl(dnsServers, cancellationToken);
        }
        catch (Exception ex) {
            // --- Fallback: use resolvconf ---
            VhLogger.Instance.LogWarning(ex, "Failed to set DNS using resolvectl. Trying fallback to resolvconf...");
            try {
                await SetDnsServersByResolvconf(dnsServers, cancellationToken);
            }
            catch (Exception fallbackEx) {
                throw new Exception(
                    $"Failed to set DNS using both resolvectl and resolvconf.\n" +
                    $"resolvectl error: {ex.Message}\n" +
                    $"resolvconf error: {fallbackEx.Message}", fallbackEx);
            }
        }
    }

    protected override void WaitForTunRead()
    {
        if (_pollFdReads != null)
            WaitForTun(_pollFdReads);
    }

    protected override void WaitForTunWrite()
    {
        if (_pollFdWrites != null)
            WaitForTun(_pollFdWrites);
    }


    private static void WaitForTun(StructPollfd[] pollFds)
    {
        while (true) {
            var result = LinuxAPI.poll(pollFds, 1, -1);
            if (result >= 0)
                break; // Success, exit loop

            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == OsConstants.Eintr)
                continue; // Poll was interrupted, retry

            throw new PInvokeException("Failed to poll the TUN device for new data.", errorCode);
        }
    }

    protected override bool WritePacket(IpPacket ipPacket)
    {
        var fd = _tunAdapterFd;
        if (fd == InvalidFd)
            return false;

        var packetBytes = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var bufferLength);

        // A TUN device writes a whole datagram or nothing, so there is no partial write to continue.
        var bytesWritten = LinuxAPI.write(fd, packetBytes, (nuint)bufferLength);
        if (bytesWritten == bufferLength)
            return true;

        // A short (but positive) write means a truncated packet was injected. This should not happen for a
        // TUN char device; drop it rather than tearing down the adapter.
        if (bytesWritten >= 0) {
            VhLogger.Instance.LogWarning(
                "Partial write to TUN device; dropping packet. Written: {Written}, Length: {Length}",
                bytesWritten, bufferLength);
            return true;
        }

        var errorCode = Marshal.GetLastWin32Error();
        return errorCode switch {
            // Buffer full (EAGAIN) or interrupted before any byte was written (EINTR): report not-sent and
            // let the parent send loop (SendPacketInternal) back off (WaitForTunWrite) and retry.
            OsConstants.Eagain or OsConstants.Eintr => false,
            _ => throw new PInvokeException("Could not write to TUN.", errorCode)
        };
    }

    protected override bool ReadPacket(byte[] buffer)
    {
        var fd = _tunAdapterFd;
        if (fd == InvalidFd)
            throw new IOException("TUN adapter is closed.");

        var bytesRead = LinuxAPI.read(fd, buffer, (nuint)buffer.Length);
        if (bytesRead > 0)
            return true;

        // check for errors
        var errorCode = Marshal.GetLastWin32Error();
        return errorCode switch {
            // No data available; the parent read loop polls (WaitForTunRead) and retries.
            OsConstants.Eagain => false,
            // Interrupted before any byte was read. Report it as "no data" and let the parent drive the
            // retry (it re-checks liveness and polls) rather than spinning here — EINTR is normal, not an
            // error, so it must not throw or count toward the I/O error threshold.
            OsConstants.Eintr => false,
            _ => throw new PInvokeException("Could not read from TUN.", errorCode)
        };
    }

    private static int OpenTunAdapter(string adapterName, bool blockingMode)
    {
        // Open the TUN device file
        var tunDeviceFd = LinuxAPI.open("/dev/net/tun", OsConstants.ORdwr);
        if (tunDeviceFd < 0)
            throw new InvalidOperationException("Failed to open TUN device.");

        // Configure the device
        var ifr = new Ifreq {
            ifr_name = adapterName,
            ifr_flags = (short)(InterfaceFlag.IffTun | InterfaceFlag.IffNoPi)
        };

        var ioctlResult = LinuxAPI.ioctl(tunDeviceFd, OsConstants.Tunsetiff, ref ifr);
        if (ioctlResult < 0) {
            LinuxAPI.close(tunDeviceFd);
            throw new PInvokeException($"Failed to configure TUN device. IoctlResult: {ioctlResult}");
        }

        if (!blockingMode) {
            if (LinuxAPI.fcntl(tunDeviceFd, OsConstants.FSetfl, OsConstants.ONonblock) < 0) {
                LinuxAPI.close(tunDeviceFd);
                throw new PInvokeException("Failed to set TUN device to non-blocking mode.");
            }
        }

        return tunDeviceFd;
    }

    protected override void BindSocketToIp(Socket socket, IPAddress ipAddress)
    {
        var adapterName = FindInterfaceNameForIp(ipAddress);
        if (string.IsNullOrEmpty(adapterName))
            throw new InvalidOperationException($"No network interface found for IP address {ipAddress}.");

        var optVal = Encoding.ASCII.GetBytes(adapterName + "\0");
        var result = LinuxAPI.setsockopt((int)socket.Handle, level: OsConstants.SolSocket,
            OsConstants.SoBindtodevice, optVal, (uint)optVal.Length);

        if (result < 0)
            throw new PInvokeException($"Failed to bind socket to device {adapterName}.");
    }

    private static string ExecuteCommand(string command)
    {
        return OsUtils.ExecuteCommand("/bin/bash", $"-c \"{command}\"");
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteCommandAsync("/bin/bash", $"-c \"{command}\"", cancellationToken);
    }

    protected override void DisposeUnmanaged()
    {
        // AdapterRemove closes the fd AND removes the NAT/forwarding iptables rules. On a normal Dispose
        // that already happened via Stop(), but the finalizer path never runs Stop(), so we must clean
        // NAT here too. A leftover MASQUERADE rule referencing a deleted interface can break the host
        // network, so cleaning it (even on the finalizer thread) is the lesser evil versus leaking it.
        if (_tunAdapterFd != InvalidFd)
            AdapterRemove();

        base.DisposeUnmanaged();
    }

    ~LinuxTunVpnAdapter()
    {
        Dispose(false);
    }
}

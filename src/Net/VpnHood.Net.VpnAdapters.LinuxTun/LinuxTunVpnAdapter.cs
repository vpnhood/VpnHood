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
    private const int IffUp = 0x1; // the interface flag ip(8) sets and clears with "link set up/down"
    private const int KeptNameLength = 10;

    // IFALIASZ less its terminator
    private const int MaxAdapterAliasLength = 255;
    private const int KeptAliasLength = 64;

    // A closed tun's number is freed only once the reader and the batch being sent have left (as WinTun's
    // session): the reader once the device's removal wakes its poll, the batch, which may sleep in the send
    // backoff, within twice MaxPacketSendDelay. A thread held off the CPU with the stop gets a moment more.
    private static readonly TimeSpan StopWaitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopRewaitTimeout = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _senderStopWaitTimeout;

    private readonly string? _adapterAlias;

    // The tun AdapterAdd made, held until AdapterRemove has deleted it: while a file holds a tun, no other can
    // take its name. The reader and the sender use a copy (_tunAdapterFd), which AdapterClose retires.
    private readonly Lock _ownTunAdapterFdLock = new();
    private int _ownTunAdapterFd = InvalidFd;
    private int _tunAdapterFd = InvalidFd;
    private int _retiredTunAdapterFd = InvalidFd;
    private int? _metric;
    private string? _primaryAdapterName;
    private bool _isResolvconfDnsSet;

    // The networks AddNat added rules for, which AdapterRemove removes. Not the adapter's networks, which a stop
    // clears, and kept after a removal: a start the stop overtook may still add rules, which its cleanup removes
    private IpNetwork? _natNetworkV4;
    private IpNetwork? _natNetworkV6;
    private StructPollfd[]? _pollFdReads;
    private StructPollfd[]? _pollFdWrites;
    private readonly byte[] _writeBuffer = new byte[0xFFFF];
    protected override bool IsSocketProtectedByBind => true;
    public override bool CanProtectSocket => !string.IsNullOrEmpty(_primaryAdapterName);

    public override bool IsNatSupported(IpVersion ipVersion) => true;
    public override bool IsAppFilterSupported => false;
    protected override string? AppPackageId => null;
    // A client takes a new network without a restart, whose teardown would let the host's traffic pass
    // outside the tunnel for its seconds; a server restarts, as its NAT names the card it started on
    protected override bool RestartAfterNetworkAddressChanged => UseNat;
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => true;

    public LinuxTunVpnAdapter(LinuxVpnAdapterSettings adapterSettings)
        : base(adapterSettings)
    {
        if (!IsValidAdapterName(AdapterName))
            throw new ArgumentException(
                $"'{AdapterName}' is not a valid Linux interface name; {nameof(GetValidAdapterName)} derives one.",
                nameof(adapterSettings));

        _adapterAlias = adapterSettings.AppId == null ? null : GetAdapterAlias(adapterSettings.AppId);
        _senderStopWaitTimeout = StopWaitTimeout + adapterSettings.MaxPacketSendDelay * 2;
    }

    // A set-down link stays listed but loses IFF_UP, and the kernel drops its routes; a fresh tun has the
    // flag while its carrier may still read down (the kernel publishes it up to a second late)
    protected override bool IsAdapterInterfaceEnabled()
    {
        try {
            var flags = Convert.ToInt32(File.ReadAllText($"/sys/class/net/{AdapterName}/flags").Trim(), 16);
            return (flags & IffUp) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            return false; // the device is gone
        }
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
    // or, from a version before tags, no tag and this adapter's name. A down one is cleared too: the
    // app's single-instance lock leaves no other run of it to hold it.
    internal static bool IsOwnLeftover(LinuxTunInfo tun, string adapterName, string? adapterAlias)
    {
        if (tun.IsHeld)
            return false;

#pragma warning disable CS0618 // Type or member is obsolete
        return tun.Alias.Length == 0
            ? IsUntaggedLeftoverToMigrate(tun, adapterName)
            : tun.Alias == adapterAlias;
#pragma warning restore CS0618 // Type or member is obsolete
    }

    // A version before tags left its tun untagged, known only by this adapter's name.
    [Obsolete("Migration (2026-09): drop a few months after it ships, with its call in IsOwnLeftover.")]
    private static bool IsUntaggedLeftoverToMigrate(LinuxTunInfo tun, string adapterName)
    {
        return tun.Name == adapterName;
    }

    // At the daemon's start, after its single-instance lock, rather than at the next connect: what a crashed
    // run left. Its tun went with its process, but the resolvconf fallback's DNS entry outlives it, which
    // would keep resolv.conf on the VPN's nameserver. Nothing here throws but for a name the kernel refuses.
    public static void RemoveLeftovers(string appId, string adapterName)
    {
        if (!IsValidAdapterName(adapterName))
            throw new ArgumentException($"'{adapterName}' is not a valid Linux interface name.", nameof(adapterName));

        if (!LinuxTunInfo.InterfaceExists(adapterName))
            TryRemoveResolvconfDns(adapterName);

#pragma warning disable CS0618 // Type or member is obsolete
        RemoveKeptTuns(appId);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    // A version before made a tun that outlived its process (ip tuntap add), so a crash left it up with
    // its routes on. Every tun that carries this app's id and no process holds - a down one too - goes,
    // its DNS entry first. An untagged one, known only by the adapter's name, the adapter's own start clears.
    [Obsolete("Migration (2026-10): drop a few months after it ships, with its call in RemoveLeftovers.")]
    private static void RemoveKeptTuns(string appId)
    {
        var adapterAlias = GetAdapterAlias(appId);
        foreach (var tun in TryListTuns().Where(x => x.Alias == adapterAlias)) {
            if (tun.IsHeld) {
                VhLogger.Instance.LogWarning("Leaving {AdapterName} alone: a process holds it.", tun.Name);
                continue;
            }

            // a name this app never gives (the constructor refuses it), which would go into root's commands
            if (!IsValidAdapterName(tun.Name)) {
                VhLogger.Instance.LogError("Leaving {AdapterName} alone: its name is not a valid adapter name.",
                    tun.Name);
                continue;
            }

            VhLogger.Instance.LogInformation("Removing {AdapterName}, a tun a previous run left.", tun.Name);
            TryRemoveResolvconfDns(tun.Name);
            VhInvoker.TryInvoke($"remove the leftover tun {tun.Name}", () =>
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
            ? $"the interface {tun.Name} is in use by another VPN ({owner}). " +
              "Stop that VPN, or give this app another name."
            : $"the interface {tun.Name} belongs to {owner}. " +
              $"If that app is gone, remove it with: sudo ip link delete {tun.Name}";
    }

    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    // The first default route's interface: the word after "dev", as a route with no gateway has no "via" before it
    // (default dev ppp0 scope link). NAT puts the name in root's commands and covers that one interface, so it
    // refuses an invalid name and a multipath route; a client only needs one.
    private static async Task<string> GetPrimaryAdapterName(bool useNat, CancellationToken cancellationToken)
    {
        // -o: a route a line, the preferred first
        var route = (await ExecuteCommandAsync("ip -o route show default", cancellationToken).Vhc()).Split('\n')[0];
        var words = route.Split([' ', '\t', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var devIndex = Array.IndexOf(words, "dev");
        var mainInterface = devIndex >= 0 && devIndex < words.Length - 1 ? words[devIndex + 1] : "";
        if (mainInterface.Length == 0)
            throw new InvalidOperationException("No active network interface found.");

        if (useNat && words.Contains("nexthop"))
            throw new InvalidOperationException(
                "NAT does not support a multipath default route: its rules name one interface.");

        if (useNat && !IsValidAdapterName(mainInterface))
            throw new InvalidOperationException(
                $"The primary network interface's name is not supported in a command: {mainInterface}");

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
        _primaryAdapterName = await GetPrimaryAdapterName(UseNat, cancellationToken).Vhc();
        VhLogger.Instance.LogDebug("Primary adapter name is {PrimaryAdapterName}", _primaryAdapterName);

        // the way clear: this adapter's own previous run, then whatever else has the name
        VhLogger.Instance.LogDebug("Clean previous tun adapter...");
        AdapterRemove();

        // Made by this process, not by ip(8), whose tun outlives the process: the kernel removes this one,
        // its routes and its DNS with it, once the process ends, by a crash too, as WinTun does its adapter.
        // Until the adapter holds it, nothing keeps the name from the app a revoke hands it to, so the
        // clearing and the making run under the lock a stop takes the tun under, and none after a stop.
        lock (_ownTunAdapterFdLock) {
            cancellationToken.ThrowIfCancellationRequested();
            ClearAdapterName();
            VhLogger.Instance.LogDebug("Creating tun adapter...");
            _ownTunAdapterFd = OpenTunAdapter(AdapterName, false);
        }

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

        // held until its delete, so the name is this adapter's tun's throughout: the DNS entry's removal and
        // the delete never meet another's
        var tunAdapterFd = TakeOwnTunAdapterFd();

        // DNS comes off before the interface, and without it too: a start a stop overtook may set it after the
        // stop removed the interface
        if (tunAdapterFd != InvalidFd || _isResolvconfDnsSet)
            RemoveOwnResolvconfDns();

        // the tun goes with its last open file, which a reader's poll may still hold: the delete takes it
        // now, and wakes that poll
        if (tunAdapterFd != InvalidFd) {
            VhLogger.Instance.LogDebug("Removing the {AdapterName} TUN adapter...", AdapterName);
            VhInvoker.TryInvoke($"remove the {AdapterName} TUN adapter", () =>
                ExecuteCommand($"ip link delete {AdapterName}"));
            LinuxAPI.close(tunAdapterFd);
        }

        // the device is gone, which wakes a reader in its poll
        FreeRetiredTunAdapterFd();

        // Remove previous NAT iptables record
        if (_natNetworkV4 != null || _natNetworkV6 != null) {
            VhLogger.Instance.LogDebug("Removing previous NAT iptables record for {AdapterName} TUN adapter...",
                AdapterName);
            if (_natNetworkV4 != null)
                TryRemoveNat(_natNetworkV4);

            if (_natNetworkV6 != null)
                TryRemoveNat(_natNetworkV6);
        }
    }

    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Opening the TUN adapter...");
        var tunAdapterFd = CopyOwnTunAdapterFd();
        _tunAdapterFd = tunAdapterFd;
        _pollFdReads = [
            new StructPollfd {
                Fd = tunAdapterFd, Events = OsConstants.Pollin
            }
        ];
        _pollFdWrites = [
            new StructPollfd {
                Fd = tunAdapterFd, Events = OsConstants.Pollout
            }
        ];

        return Task.CompletedTask;
    }

    // The reader's and the sender's copy of the tun AdapterAdd made, never a child's either. Under the lock
    // AdapterRemove takes that one under, so its number is not closed under the copying.
    private int CopyOwnTunAdapterFd()
    {
        lock (_ownTunAdapterFdLock) {
            if (_ownTunAdapterFd == InvalidFd)
                throw new InvalidOperationException("The TUN adapter has not been added.");

            var fd = LinuxAPI.fcntl(_ownTunAdapterFd, OsConstants.FDupfdCloexec, 0);
            return fd >= 0 ? fd : throw new PInvokeException("Could not open the TUN adapter.");
        }
    }

    private int TakeOwnTunAdapterFd()
    {
        lock (_ownTunAdapterFdLock) {
            var fd = _ownTunAdapterFd;
            _ownTunAdapterFd = InvalidFd;
            return fd;
        }
    }

    // The reader and the sender read the copy's number from the field, so one may still hold it after the
    // clearing; were the number closed, the next file the process opens could take it and get their read or
    // write. /dev/null takes the tun's place under it in one step (dup2), which gives a late read an end and a
    // late write a sink; AdapterRemove frees the number once both have left.
    protected override void AdapterClose()
    {
        var fd = Interlocked.Exchange(ref _tunAdapterFd, InvalidFd);
        if (fd == InvalidFd)
            return;

        // failing that, the number stays the tun's until then: a late read or write meets the tun, never another file
        if (!TryReplaceByNull(fd))
            VhLogger.Instance.LogWarning(
                "Could not put /dev/null under the TUN adapter's descriptor; it stays the tun's until its users leave.");

        _retiredTunAdapterFd = fd;
    }

    private static bool TryReplaceByNull(int fd)
    {
        var nullFd = LinuxAPI.open("/dev/null", OsConstants.ORdwr);
        if (nullFd < 0)
            return false;

        var result = LinuxAPI.dup2(nullFd, fd);
        LinuxAPI.close(nullFd);
        return result >= 0;
    }

    // One that does not leave in time keeps the number until the process exits. A finalizer finds neither, as
    // either keeps the adapter alive, so its waits return at once.
    private void FreeRetiredTunAdapterFd()
    {
        var fd = Interlocked.Exchange(ref _retiredTunAdapterFd, InvalidFd);
        if (fd == InvalidFd)
            return;

        var readerLeft = WaitForReader(StopWaitTimeout);
        var senderLeft = WaitForSender(_senderStopWaitTimeout);
        readerLeft = readerLeft || WaitForReader(StopRewaitTimeout);
        senderLeft = senderLeft || WaitForSender(StopRewaitTimeout);
        if (!readerLeft || !senderLeft) {
            VhLogger.Instance.LogError(
                "The TUN {Thread} did not leave in time, so the adapter's descriptor stays taken until the process exits.",
                senderLeft ? "reader" : readerLeft ? "sender" : "reader and sender");
            return;
        }

        LinuxAPI.close(fd);
    }

    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // remove old NAT rule if any
        TryRemoveNat(ipNetwork);

        // recorded before its rules, so a removal finds a half-added set too
        if (ipNetwork.IsV4)
            _natNetworkV4 = ipNetwork;
        else
            _natNetworkV6 = ipNetwork;

        // Cancellable, unlike resolvconf's add: a rule a cancelled start's command adds after the cleanup serves
        // only the removed tun, and the next start deletes it; uncancelled, -w 5 could hold such a start 15 s.
        var iptables = IpTables(ipNetwork);
        await ExecuteCommandAsync(
            $"{iptables} -t nat -A POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE",
            cancellationToken).Vhc();

        await ExecuteCommandAsync($"{iptables} -A FORWARD -i {AdapterName} -o {_primaryAdapterName} -j ACCEPT",
            cancellationToken).Vhc();

        await ExecuteCommandAsync(
            $"{iptables} -A FORWARD -i {_primaryAdapterName} -o {AdapterName} -m state --state RELATED,ESTABLISHED -j ACCEPT",
            cancellationToken).Vhc();
    }

    // The rules AddNat adds, each by its own spec: restoring a whole table would also drop a rule
    // another tool, such as a firewall or Docker, adds meanwhile. The spec names the current primary
    // interface, so rules an earlier run added for another one stay; left so: they only pass and NAT
    // the tunnel's own traffic through that interface.
    private void TryRemoveNat(IpNetwork ipNetwork)
    {
        var iptables = IpTables(ipNetwork);
        TryDeleteRule(iptables, $"-t nat -D POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE");
        TryDeleteRule(iptables, $"-D FORWARD -i {AdapterName} -o {_primaryAdapterName} -j ACCEPT");
        TryDeleteRule(iptables,
            $"-D FORWARD -i {_primaryAdapterName} -o {AdapterName} -m state --state RELATED,ESTABLISHED -j ACCEPT");
    }

    // -w 5: up to 5 s for the xtables lock another tool, such as a firewall or Docker, may hold, rather
    // than fail at once; the nft backend has no such lock and accepts it all the same.
    private static string IpTables(IpNetwork ipNetwork) => ipNetwork.IsV4 ? "iptables -w 5" : "ip6tables -w 5";

    // every copy: a delete prints nothing, and fails once none is left
    private static void TryDeleteRule(string iptables, string rule)
    {
        string? res = "";
        while (res != null)
            res = VhInvoker.TryInvoke("Remove NAT rule", () => ExecuteCommand($"{iptables} {rule}"));
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

    // Never cut off: an entry the command made after a stop's cleanup looked would stay, its flag unset, until
    // the next connect clears it. -x first: openresolv uses the newest exclusive entry alone; Debian's
    // resolvconf, which has no exclusive mode, refuses the command, so the start fails closed.
    private async Task SetDnsServersByResolvconf(IEnumerable<IPAddress> dnsServers)
    {
        var allDns = string.Join(" ", dnsServers.Select(x => x.ToString()));
        await ExecuteCommandAsync($"printf 'nameserver %s\\n' {allDns} | resolvconf -x -m 0 -a {AdapterName}",
            CancellationToken.None).Vhc();
        _isResolvconfDnsSet = true;
    }

    // The resolvconf fallback's entry outlives the interface, where resolvectl's per-link DNS goes
    // with it: left behind, resolv.conf keeps the VPN's nameserver after a disconnect. So it comes
    // off before any interface this owner removes - its own at a disconnect, a leftover at a start -
    // and never for a name another owner holds.
    private static string RemoveResolvconfDnsCommand(string adapterName)
    {
        return $"if command -v resolvconf >/dev/null; then resolvconf -d {adapterName} -f; fi";
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

    // Usually there is nothing to remove, which -f lets pass; systemd-resolved's resolvconf still fails
    // without its service while the interface exists.
    private static void TryRemoveResolvconfDns(string adapterName)
    {
        VhInvoker.TryInvoke($"remove the resolvconf DNS entry of {adapterName}", () =>
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
            await SetDnsServersByResolvectl(dnsServers, cancellationToken).Vhc();
        }
        // a cancelled start, whatever ended resolvectl: no fallback, which runs uncancelled
        catch (Exception) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            // --- Fallback: use resolvconf ---
            VhLogger.Instance.LogWarning(ex, "Failed to set DNS using resolvectl. Trying fallback to resolvconf...");
            try {
                await SetDnsServersByResolvconf(dnsServers).Vhc();
            }
            // Fail-closed: a VPN up without its DNS servers sends the device's DNS around it. A NetworkManager
            // box without systemd-resolved (Arch's default) gets here, as its resolvconf is systemd-resolved's
            catch (Exception fallbackEx) {
                throw new Exception(
                    "Could not set the VPN's DNS servers. Install and turn on systemd-resolved " +
                    "(systemctl enable --now systemd-resolved) and link /etc/resolv.conf to " +
                    "/run/systemd/resolve/stub-resolv.conf.\n" +
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
        // Open the TUN device file; never a child's, which would keep the tun past this process
        var tunDeviceFd = LinuxAPI.open("/dev/net/tun", OsConstants.ORdwr | OsConstants.OCloexec);
        if (tunDeviceFd < 0)
            throw new InvalidOperationException("Failed to open TUN device.");

        // A new tun, as ip(8) makes one: one that already has the name, made since the start's checks, is
        // refused (EBUSY) rather than taken over
        var ifr = new Ifreq {
            ifr_name = adapterName,
            ifr_flags = unchecked((short)(InterfaceFlag.IffTun | InterfaceFlag.IffNoPi | InterfaceFlag.IffTunExcl))
        };

        if (LinuxAPI.ioctl(tunDeviceFd, OsConstants.Tunsetiff, ref ifr) < 0) {
            var errorCode = Marshal.GetLastWin32Error(); // before the close, which would replace it
            LinuxAPI.close(tunDeviceFd);
            throw new PInvokeException($"Could not create the TUN device {adapterName}. ErrorCode: {errorCode}", errorCode);
        }

        if (!blockingMode) {
            if (LinuxAPI.fcntl(tunDeviceFd, OsConstants.FSetfl, OsConstants.ONonblock) < 0) {
                var errorCode = Marshal.GetLastWin32Error(); // before the close, which would replace it
                LinuxAPI.close(tunDeviceFd);
                throw new PInvokeException("Failed to set TUN device to non-blocking mode.", errorCode);
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
        return OsUtils.ExecuteShellCommand(command);
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteShellCommandAsync(command, cancellationToken);
    }

    protected override void DisposeUnmanaged()
    {
        // AdapterRemove closes the fd AND removes the NAT/forwarding iptables rules. On a normal Dispose
        // that already happened via Stop(), but the finalizer path never runs Stop(), so we must clean
        // NAT here too. A leftover MASQUERADE rule referencing a deleted interface can break the host
        // network, so cleaning it (even on the finalizer thread) is the lesser evil versus leaking it.
        if (_tunAdapterFd != InvalidFd || _ownTunAdapterFd != InvalidFd)
            AdapterRemove();

        base.DisposeUnmanaged();
    }

    ~LinuxTunVpnAdapter()
    {
        Dispose(false);
    }
}

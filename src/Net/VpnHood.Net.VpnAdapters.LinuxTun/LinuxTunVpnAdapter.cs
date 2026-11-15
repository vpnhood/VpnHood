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

/// <summary>
/// Provides the VPN adapter for Linux, on a TUN device of the process's own: it gives the interface its addresses,
/// routes, MTU and DNS servers, and, for a server, NAT for IPv4 and IPv6 through iptables.
/// </summary>
/// <remarks>
/// <para>
/// It needs root, and runs <c>ip</c>, <c>sysctl</c>, <c>resolvectl</c> or <c>resolvconf</c>, <c>iptables</c> and
/// <c>ip6tables</c>, <c>nft</c> and <c>udevadm</c> through bash; a canceled step stops waiting for its command, which
/// runs on. The kernel removes the TUN device, with its routes and systemd-resolved's DNS servers for it, when the
/// process ends, a crash included. A resolvconf entry and the DNS redirect stay, which <see cref="RemoveLeftovers"/>
/// clears, and so do the NAT chains, of both IP versions, until the next start that uses NAT.
/// </para>
/// <para>
/// The interface's alias carries a tag made from the app's ID, when there is one (<see cref="VpnAdapterSettings.AppId"/>),
/// which tells a start whose an interface of its name is. A TUN device of the name that carries the tag is taken for
/// one the app left and removed unless a process is known to hold it: a device that is down shows no holder, and the
/// app's single-instance lock is relied on. One with no tag, as versions before tags made, is taken so too, but only
/// while it is up, where it shows whether a process holds it. Any other interface of the name refuses the start. NAT,
/// a server's, turns on forwarding for the whole host, for the IP versions it translates, and leaves it on; for IPv6
/// an interface that takes router advertisements goes on taking them (<c>accept_ra</c> 2), but for the adapter's own.
/// There is no app filter.
/// </para>
/// </remarks>
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

    // the longest packet the parser takes: an IPv6 one, 40 bytes of header and the 65,535 its length field counts
    private const int MaxPacketLength = 40 + 0xFFFF;

    // a folder of settings for each interface, and "all" and "default" beside them
    private const string Ipv6InterfaceSettingsFolder = "/proc/sys/net/ipv6/conf";

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
    private bool _isResolvconfDnsSet;

    // The VPN's servers while forwarded DNS goes to them. Set before the rule, so a stop's cleanup finds the rule of a
    // start it overtook; a network change rewrites the rule only while they are set.
    private readonly Lock _dnsRedirectLock = new();
    private IPAddress[]? _dnsRedirectServers;

    // Whether AddNat has made the adapter's chains of an IP version, which AdapterRemove removes, and the record
    // with them. A record kept after its removal would take another adapter's chains: one that has the name by
    // the time this one starts again, before the start finds the name taken.
    private bool _isNatAddedV4;
    private bool _isNatAddedV6;
    private StructPollfd[]? _pollFdReads;
    private StructPollfd[]? _pollFdWrites;
    private readonly byte[] _writeBuffer = new byte[MaxPacketLength];

    /// <inheritdoc/>
    /// <value>Always <see langword="true"/>: <see cref="BindSocketToIp"/> binds a socket to an interface.</value>
    protected override bool IsSocketProtectedByBind => true;

    /// <inheritdoc/>
    /// <remarks>Always <see langword="true"/>: iptables and ip6tables translate both IP versions.</remarks>
    public override bool IsNatSupported(IpVersion ipVersion) => true;

    /// <inheritdoc/>
    /// <value>Always <see langword="false"/>.</value>
    public override bool IsAppFilterSupported => false;

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: there is no app filter.</value>
    protected override string? AppPackageId => null;

    /// <inheritdoc/>
    /// <value>
    /// <see langword="true"/> when the adapter uses NAT, as a server's does, whose rules name the interface it started
    /// on; <see langword="false"/> for a client's, which takes a new network without a restart, whose teardown would
    /// let the host's traffic pass outside the tunnel for its seconds.
    /// </value>
    protected override bool RestartAfterNetworkAddressChanged => UseNat;

    /// <inheritdoc/>
    /// <value>Always <see langword="true"/>.</value>
    protected override bool CheckAdapterInterfaceAfterNetworkChanged => true;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxTunVpnAdapter"/> class.
    /// </summary>
    /// <param name="adapterSettings">
    /// The adapter's settings, whose <see cref="VpnAdapterSettings.AdapterName"/> must be a valid interface name
    /// (<see cref="IsValidAdapterName"/>), as <see cref="GetValidAdapterName"/> derives from any name.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The settings' blocking timeout is negative or too long, or their queue capacity is not above zero.
    /// </exception>
    /// <exception cref="ArgumentException">The adapter's name is not a valid interface name.</exception>
    public LinuxTunVpnAdapter(LinuxVpnAdapterSettings adapterSettings)
        : base(ValidateSettings(adapterSettings))
    {
        _adapterAlias = adapterSettings.AppId == null ? null : GetAdapterAlias(adapterSettings.AppId);
        _senderStopWaitTimeout = StopWaitTimeout + adapterSettings.MaxPacketSendDelay * 2;

        // a client keeps its adapter on a new network, whose addresses the redirect names
        PrimaryNetworkChanged += (_, _) => ApplyDnsRedirect();
    }

    // Before the base is made, which starts its send loop and its watch of the host's network: a constructor that
    // threw after it left that at work in an instance no one could dispose
    private static LinuxVpnAdapterSettings ValidateSettings(LinuxVpnAdapterSettings adapterSettings)
    {
        return IsValidAdapterName(adapterSettings.AdapterName)
            ? adapterSettings
            : throw new ArgumentException(
                $"'{adapterSettings.AdapterName}' is not a valid Linux interface name; {nameof(GetValidAdapterName)} derives one.",
                nameof(adapterSettings));
    }

    /// <inheritdoc/>
    /// <returns>
    /// <see langword="true"/> if the interface is there and up; <see langword="false"/> if it is gone, or set down.
    /// </returns>
    /// <remarks>
    /// It reads the interface's flags from sysfs: a link set down keeps its address, which the base's look finds,
    /// while the kernel drops its routes. Its carrier is not consulted, as a new TUN device may report it down for up to
    /// a second.
    /// </remarks>
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

    /// <summary>
    /// Determines whether a name is one the adapter gives its interface: 1 to 15 characters, each an ASCII letter or
    /// digit, <c>_</c>, <c>-</c> or <c>.</c>, not starting with <c>-</c>, and neither <c>.</c> nor <c>..</c>.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> if the name is valid; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// The kernel refuses a longer name, and some characters; a name starting with <c>-</c> would read as an option
    /// on a command line.
    /// </remarks>
    public static bool IsValidAdapterName(string name)
    {
        return name.Length is > 0 and <= MaxAdapterNameLength &&
               name is not ("." or "..") &&
               name[0] != '-' &&
               name.All(IsValidAdapterNameChar);
    }

    /// <summary>
    /// Derives a valid interface name (<see cref="IsValidAdapterName"/>) from any name.
    /// </summary>
    /// <param name="name">The name, such as the app's.</param>
    /// <returns>
    /// The name itself when it is valid; otherwise its first ten valid characters less any leading <c>-</c> or
    /// <c>.</c>, a dash and the first four hex digits of the SHA-256 hash of the whole name, or <c>vh-</c> and those
    /// digits when no character is left. The same name always gives the same result.
    /// </returns>
    // VpnHoodClient, VpnHoodConnect and VpnHoodServer pass unchanged. The hash keeps apart two long names that share
    // a prefix; the app id, not the name, decides whose a tun is
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
    // or, from a version before tags, no tag and this adapter's name. A tagged one that is down is cleared
    // too: the app's single-instance lock leaves no other run of it to hold it.
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

    // A version before tags left its tun untagged, known only by this adapter's name, and up. One that is down
    // shows no holder, and with no tag nothing says it is this app's: another app's, in use, would go under it.
    [Obsolete("Migration (2026-09): drop a few months after it ships, with its call in IsOwnLeftover.")]
    private static bool IsUntaggedLeftoverToMigrate(LinuxTunInfo tun, string adapterName)
    {
        return tun.Name == adapterName && !tun.IsDown;
    }

    /// <summary>
    /// Removes what an earlier run of the app left on the host: its resolvconf DNS entry and DNS redirect, when no
    /// interface has the adapter's name, and every TUN device with a valid name that carries the app's tag as its alias
    /// and that no process is known to hold.
    /// </summary>
    /// <param name="appId">The app's ID, as in <see cref="VpnAdapterSettings.AppId"/>.</param>
    /// <param name="adapterName">The adapter's name, as in <see cref="VpnAdapterSettings.AdapterName"/>.</param>
    /// <remarks>
    /// Call it when the app starts, once no other instance of it can run, rather than at the next connect: a crashed
    /// run's TUN device went with its process, but its resolvconf entry would keep <c>/etc/resolv.conf</c> on the VPN's
    /// servers, and the kernel keeps the redirect's tables. The TUN devices go for versions that made them with
    /// <c>ip tuntap</c>, which outlive their process. It needs root; a failure is logged, not thrown.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="adapterName"/> is not a valid interface name.</exception>
    // At the daemon's start, after its single-instance lock
    public static void RemoveLeftovers(string appId, string adapterName)
    {
        if (!IsValidAdapterName(adapterName))
            throw new ArgumentException($"'{adapterName}' is not a valid Linux interface name.", nameof(adapterName));

        if (!LinuxTunInfo.InterfaceExists(adapterName)) {
            TryRemoveResolvconfDns(adapterName);
            TryRemoveDnsRedirect(adapterName, LogLevel.Debug);
        }

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
            VhInvoker.TryInvoke($"remove the leftover tun {tun.Name}", LogLevel.Warning, () =>
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

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: LinuxTun has no app filter.</exception>
    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: LinuxTun has no app filter.</exception>
    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    // The interface NAT sends an IP version's traffic out by: that of the host's first default route of the version.
    // Of the network's own version, as a host's IPv6 may leave by another interface than its IPv4, or be all it has.
    private static async Task<string> GetNatInterfaceName(AddressFamily addressFamily,
        CancellationToken cancellationToken)
    {
        // -o: a route a line, the preferred first
        var ipVersion = addressFamily == AddressFamily.InterNetwork ? "-4" : "-6";
        var routes = await ExecuteCommandAsync($"ip {ipVersion} -o route show default", cancellationToken).Vhc();
        return ParseDefaultRouteInterface(routes);
    }

    // The word after "dev", as a route with no gateway has no "via" before it (default dev ppp0 scope link). NAT puts
    // the name in root's commands and covers that one interface, so an invalid name and a multipath route are
    // refused.
    internal static string ParseDefaultRouteInterface(string routes)
    {
        var route = routes.Split('\n')[0];
        var words = route.Split([' ', '\t', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var devIndex = Array.IndexOf(words, "dev");
        var mainInterface = devIndex >= 0 && devIndex < words.Length - 1 ? words[devIndex + 1] : "";
        if (words.Contains("nexthop"))
            throw new InvalidOperationException(
                "NAT does not support a multipath default route: its rules name one interface.");

        if (mainInterface.Length == 0)
            throw new InvalidOperationException("NAT found no default route to send its traffic out by.");

        if (!IsValidAdapterName(mainInterface))
            throw new InvalidOperationException(
                $"The primary network interface's name is not supported in a command: {mainInterface}");

        return mainInterface;
    }

    private static string? FindInterfaceNameForIp(IPAddress ip)
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

    /// <inheritdoc/>
    /// <remarks>
    /// It removes what this adapter's last run left, and tries to keep NetworkManager off the interface with a udev
    /// rule under <c>/run</c>, without which a desktop announces the VPN's coming and going as a network connection; a
    /// failure there is logged. Then it makes the TUN device, after removing a TUN device of the name that the app left
    /// (see the class's remarks), writes the app's tag as its alias, and sets the interface up. A start that uses NAT
    /// removes, once the device is made, the NAT chains of the adapter's name that an earlier run left, as one that
    /// ended in a crash does, of both IP versions; a start without NAT leaves them.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// An interface of the adapter's name is not a TUN device, or is another app's or held by a process; or
    /// <c>/dev/net/tun</c> could not be opened.
    /// </exception>
    /// <exception cref="PInvokeException">
    /// The kernel refused the TUN device, as without CAP_NET_ADMIN, or its non-blocking mode.
    /// </exception>
    /// <exception cref="ExternalException">A command failed.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">bash could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    protected override async Task AdapterAdd(CancellationToken cancellationToken)
    {
        // a run's own: one that sets no metric adds its routes with the system's, not the last run's
        _metric = null;

        // the way clear: this adapter's own previous run, then whatever else has the name
        VhLogger.Instance.LogDebug("Clean previous tun adapter...");
        AdapterRemove();

        await ExcludeFromNetworkManager(cancellationToken).Vhc();

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

        // The NAT an earlier run left, as one that ended in a crash, whose chains are still at work. Of both IP
        // versions, whatever this run's are: left to AddNat, the version this run does not translate stayed. Once
        // the name is this adapter's, never before: the chains are known by the name alone, and a start that is
        // refused the name must not take its owner's. Only with NAT: a start without it runs no iptables.
        if (UseNat) {
            TryRemoveNat(AddressFamily.InterNetwork);
            TryRemoveNat(AddressFamily.InterNetworkV6);
        }

        // The alias derived from the app id lets a later start tell this tun from another app's.
        if (_adapterAlias != null)
            await ExecuteCommandAsync($"ip link set dev {AdapterName} alias {_adapterAlias}", cancellationToken).Vhc();

        // Bring up the interface
        VhLogger.Instance.LogDebug("Bringing up the TUN...");
        await ExecuteCommandAsync($"ip link set {AdapterName} up", cancellationToken).Vhc();
    }

    // NetworkManager adopts a new interface it does not manage as "connected (externally)", and a desktop's
    // network tray then announces the tun's coming and going as a connection made and lost (Lubuntu's
    // nm-tray: "No longer connected to 802-3-ethernet ''" at every disconnect). NetworkManager waits for udev
    // before it looks at a device, so a udev rule marking the name NM_UNMANAGED keeps it off from the start;
    // nmcli once the tun exists would come after the announcement. The rule is in /run, gone at the next
    // boot, and written once per boot: the name is this app's alone (ClearAdapterName). Without it the VPN
    // works the same, so a failure is a warning; a machine without udev (a container) has no
    // NetworkManager to keep off.
    private async Task ExcludeFromNetworkManager(CancellationToken cancellationToken)
    {
        if (!Directory.Exists("/run/udev"))
            return;

        const string rulesFolder = "/run/udev/rules.d";
        var rulesFile = Path.Combine(rulesFolder, $"90-{AdapterName}.rules");
        var rule = $"SUBSYSTEM==\"net\", KERNEL==\"{AdapterName}\", ENV{{NM_UNMANAGED}}=\"1\"\n";
        try {
            if (File.Exists(rulesFile) && await File.ReadAllTextAsync(rulesFile, cancellationToken).Vhc() == rule)
                return;

            VhLogger.Instance.LogDebug("Keeping NetworkManager off {AdapterName}...", AdapterName);
            Directory.CreateDirectory(rulesFolder);
            await FileUtils.WriteAllTextAtomicAsync(rulesFile, rule, cancellationToken).Vhc();
            await ExecuteCommandAsync("udevadm control --reload", cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "Could not keep NetworkManager off {AdapterName}; the desktop may announce the VPN as a network connection.",
                AdapterName);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// It removes only what this adapter added: its resolvconf entry and DNS redirect, the TUN device it made, and its
    /// NAT, which is in chains of the adapter's own: the jumps to them and the chains go, and no rule of another
    /// tool's. A failure is logged, not thrown.
    /// </para>
    /// <para>
    /// Before the device's descriptor is freed, it waits up to a second for the reader to leave, which the device's
    /// removal wakes, and up to a second plus twice <see cref="VpnAdapterSettings.MaxPacketSendDelay"/> for a batch
    /// being sent, then 100 ms more for each that is late; a reader or sender that stops the adapter itself is not
    /// waited for. When one does not leave in time, the descriptor stays taken until the process exits, which is
    /// logged as an error.
    /// </para>
    /// </remarks>
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

        RemoveDnsRedirect();

        // the tun goes with its last open file, which a reader's poll may still hold: the delete takes it
        // now, and wakes that poll
        if (tunAdapterFd != InvalidFd) {
            VhLogger.Instance.LogDebug("Removing the {AdapterName} TUN adapter...", AdapterName);
            VhInvoker.TryInvoke($"remove the {AdapterName} TUN adapter", LogLevel.Warning, () =>
                ExecuteCommand($"ip link delete {AdapterName}"));
            LinuxAPI.close(tunAdapterFd);
        }

        // the device is gone, which wakes a reader in its poll
        FreeRetiredTunAdapterFd();

        // the NAT this adapter added, and its record (see the fields)
        if (_isNatAddedV4) {
            _isNatAddedV4 = false;
            TryRemoveNat(AddressFamily.InterNetwork);
        }

        if (_isNatAddedV6) {
            _isNatAddedV6 = false;
            TryRemoveNat(AddressFamily.InterNetworkV6);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reader and the sender use a copy of the TUN device's descriptor, which <see cref="AdapterClose"/> retires.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The TUN device has not been made, or a stop has removed it.</exception>
    /// <exception cref="PInvokeException">The descriptor could not be copied.</exception>
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
    /// <inheritdoc/>
    /// <remarks>
    /// It puts <c>/dev/null</c> in the place of the reader's and the sender's descriptor, so a late read meets the end
    /// of a file and a late write a sink, never a file that took the number; <see cref="AdapterRemove"/> frees it. When
    /// <c>/dev/null</c> cannot be put there, which is logged, the descriptor stays the TUN device's until then.
    /// </remarks>
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

    /// <inheritdoc/>
    /// <remarks>
    /// It finds the interface of the host's first default route of the network's IP version, and turns on forwarding
    /// for that version, for the whole host, which stays on. For IPv6 it first sets <c>accept_ra</c> to 2 on every
    /// interface that takes router advertisements now, which is one that does not forward yet and has it at 1, other
    /// than the adapter's own and the loopback; that stays too. The kernel otherwise drops the routes the host has
    /// from router advertisements, and takes no more of them. Then it makes two chains of the adapter's own with
    /// <c>iptables</c>, or <c>ip6tables</c> for IPv6, named <c>vpnhood_nat_</c> and the adapter's name: one in the
    /// nat table, with a masquerade of the network's traffic out of that interface, and one in the filter table,
    /// with two forwarding rules between that interface and the tunnel's, the way back for related and established
    /// connections only. POSTROUTING and FORWARD each get a jump to its chain. Each command waits up to 5 seconds
    /// for the xtables lock another tool may hold. A canceled start's command may still add its rule after the
    /// cleanup; the next start that uses NAT removes it (see <see cref="AdapterAdd"/>).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The host has no default route of the network's IP version, or a multipath one, or one whose interface has a name
    /// not valid here.
    /// </exception>
    /// <exception cref="ExternalException">A command failed.</exception>
    /// <exception cref="IOException">An interface's <c>accept_ra</c> could not be read or set.</exception>
    /// <exception cref="UnauthorizedAccessException">An interface's <c>accept_ra</c> could not be set.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">bash could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        var natInterfaceName = await GetNatInterfaceName(ipNetwork.AddressFamily, cancellationToken).Vhc();

        // NAT forwards, and nothing else here does: turned on by every start, a client's too, forwarding stayed on
        // for the whole host. So here alone, and for this IP version alone. Left on at a stop: another service may
        // count on it by then.
        if (ipNetwork.IsV6)
            await KeepRouterAdvertisements(cancellationToken).Vhc();

        var forwardingKey = ipNetwork.IsV4 ? "net.ipv4.ip_forward" : "net.ipv6.conf.all.forwarding";
        await ExecuteCommandAsync($"sysctl -w {forwardingKey}=1", cancellationToken).Vhc();

        // recorded before its chains, so a removal finds a half-made set too
        if (ipNetwork.IsV4)
            _isNatAddedV4 = true;
        else
            _isNatAddedV6 = true;

        // Cancellable, unlike resolvconf's add: a rule a cancelled start's command adds after the cleanup serves
        // only the removed tun, and the next start deletes it; uncancelled, -w 5 could hold such a start 35 s.
        foreach (var command in CreateNatCommands(AdapterName, ipNetwork, natInterfaceName))
            await ExecuteCommandAsync(command, cancellationToken).Vhc();
    }

    // As IPv6 forwarding goes on, the kernel drops the default route an interface has from a router advertisement
    // and takes no more of them where accept_ra is 1: a host that has its IPv6 route that way lost it for good. 2
    // keeps both under forwarding, so it is set first, on every interface that takes advertisements now: the switch
    // is for all of them, and a second uplink loses its route as the first does. Left at a stop, as forwarding is.
    private async Task KeepRouterAdvertisements(CancellationToken cancellationToken)
    {
        foreach (var interfaceFolder in Directory.GetDirectories(Ipv6InterfaceSettingsFolder)) {
            // Not the adapter's own: its advertisements would be a VPN client's, which forwarding keeps out.
            // "all" and "default" are no interfaces, and default is what an interface made later starts with,
            // the adapter's next one too.
            var interfaceName = Path.GetFileName(interfaceFolder);
            if (interfaceName is "all" or "default" or "lo" || interfaceName == AdapterName)
                continue;

            try {
                await KeepRouterAdvertisements(interfaceFolder, interfaceName, cancellationToken).Vhc();
            }
            catch (IOException) when (!Directory.Exists(interfaceFolder)) {
                // an interface that has gone since the list was made, as a container's does, has no route to keep
            }
        }
    }

    // Only where the interface does not forward yet, which the switch for all of them is about to change: one that
    // forwards already takes no advertisement, by its host's own arrangement.
    private static async Task KeepRouterAdvertisements(string interfaceFolder, string interfaceName,
        CancellationToken cancellationToken)
    {
        var forwarding = await File.ReadAllTextAsync(Path.Combine(interfaceFolder, "forwarding"), cancellationToken).Vhc();
        if (forwarding.Trim() != "0")
            return;

        var acceptRaPath = Path.Combine(interfaceFolder, "accept_ra");
        var acceptRa = await File.ReadAllTextAsync(acceptRaPath, cancellationToken).Vhc();
        if (acceptRa.Trim() != "1")
            return;

        VhLogger.Instance.LogInformation(
            "Setting accept_ra to 2 on {InterfaceName}, so it keeps its route from router advertisements under IPv6 forwarding.",
            interfaceName);
        await File.WriteAllTextAsync(acceptRaPath, "2", cancellationToken).Vhc();
    }

    // The adapter's NAT of a network: a chain of its own in each table, filled before the jump that leads to it. In
    // chains, not among the other tools' rules: a rule there is known as the adapter's by its spec alone, so removing
    // it took a rule of the same spec that a firewall or an administrator wrote.
    internal static IReadOnlyList<string> CreateNatCommands(string adapterName, IpNetwork ipNetwork,
        string natInterfaceName)
    {
        var iptables = IpTables(ipNetwork.AddressFamily);
        var chain = GetNatChainName(adapterName);
        return [
            $"{iptables} -t nat -N {chain}",
            $"{iptables} -t nat -A {chain} -s {ipNetwork} -o {natInterfaceName} -j MASQUERADE",
            $"{iptables} -t nat -A POSTROUTING -j {chain}",
            $"{iptables} -N {chain}",
            $"{iptables} -A {chain} -i {adapterName} -o {natInterfaceName} -j ACCEPT",
            $"{iptables} -A {chain} -i {natInterfaceName} -o {adapterName} -m state --state RELATED,ESTABLISHED -j ACCEPT",
            $"{iptables} -A FORWARD -j {chain}"
        ];
    }

    // 12 characters and the name's 15 at most: iptables takes a chain name of up to 28
    private static string GetNatChainName(string adapterName) => $"vpnhood_nat_{adapterName}";

    // The adapter's NAT of an IP version: the jumps to its chains, every copy, then the chains with what is in them,
    // whatever network and interface they were made for. Each step alone, as most find nothing to remove.
    private void TryRemoveNat(AddressFamily addressFamily)
    {
        VhLogger.Instance.LogDebug("Removing the NAT of the {AdapterName} TUN adapter...", AdapterName);
        var iptables = IpTables(addressFamily);
        var chain = GetNatChainName(AdapterName);
        TryDeleteRule(iptables, $"-t nat -D POSTROUTING -j {chain}");
        TryRemoveChain(iptables, "-t nat", chain);
        TryDeleteRule(iptables, $"-D FORWARD -j {chain}");
        TryRemoveChain(iptables, "", chain);
    }

    // -w 5: up to 5 s for the xtables lock another tool, such as a firewall or Docker, may hold, rather
    // than fail at once; the nft backend has no such lock and accepts it all the same.
    private static string IpTables(AddressFamily addressFamily) =>
        addressFamily == AddressFamily.InterNetwork ? "iptables -w 5" : "ip6tables -w 5";

    // every copy: a delete prints nothing, and fails once none is left
    private static void TryDeleteRule(string iptables, string rule)
    {
        string? res = "";
        while (res != null)
            res = VhInvoker.TryInvoke("remove a NAT rule", LogLevel.Debug, () => ExecuteCommand($"{iptables} {rule}"));
    }

    // emptied first: a chain with a rule in it cannot be deleted
    private static void TryRemoveChain(string iptables, string table, string chain)
    {
        VhInvoker.TryInvoke("empty a NAT chain", LogLevel.Debug, () => ExecuteCommand($"{iptables} {table} -F {chain}"));
        VhInvoker.TryInvoke("remove a NAT chain", LogLevel.Debug, () => ExecuteCommand($"{iptables} {table} -X {chain}"));
    }

    /// <inheritdoc/>
    /// <exception cref="ExternalException"><c>ip addr add</c> failed.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">bash could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    protected override async Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        await ExecuteCommandAsync($"ip addr add {ipNetwork} dev {AdapterName}",
            cancellationToken).Vhc();
    }

    /// <inheritdoc/>
    /// <remarks>The route takes the metric <see cref="SetMetric"/> kept in this run, if any.</remarks>
    /// <exception cref="ExternalException"><c>ip route add</c> failed, as for a route the table has already.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">bash could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    protected override async Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"ip route add {ipNetwork} dev {AdapterName}"
            : $"ip -6 route add {ipNetwork} dev {AdapterName}";

        if (_metric != null)
            command += $" metric {_metric}";

        await ExecuteCommandAsync(command, cancellationToken).Vhc();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It keeps the metric for the routes the start adds next, of both IP versions, and changes no route already
    /// there. The next start forgets it.
    /// </remarks>
    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        _metric = metric;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing.</remarks>
    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It sets the interface's MTU, for both IP versions.</remarks>
    /// <exception cref="ExternalException"><c>ip link set</c> failed.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">bash could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    protected override async Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        var command = $"ip link set dev {AdapterName} mtu {mtu}";
        await ExecuteCommandAsync(command, cancellationToken).Vhc();
    }

    private const string ResolvConfPath = "/etc/resolv.conf";
    private static readonly IPAddress[] ResolvedStubServers = [IPAddress.Parse("127.0.0.53"), IPAddress.Parse("127.0.0.54")];

    // What systemd-resolved knows, every link's servers: not its stub, which a container cannot reach
    private const string ResolvedServersPath = "/run/systemd/resolve/resolv.conf";

    private const string DnsServersError =
        "Could not set the VPN's DNS servers. Run the app's install command again, which turns on " +
        "systemd-resolved and links /etc/resolv.conf to it.";

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
        VhInvoker.TryInvoke($"remove the resolvconf DNS entry of {adapterName}", LogLevel.Debug, () =>
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

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// With no servers, it does nothing. Otherwise it gives them to systemd-resolved, through <c>resolvectl</c>, as
    /// the interface's servers with the routing domain <c>~.</c>, which sends them every name no other link has a more
    /// specific domain for; when that fails, it gives them to <c>resolvconf</c> as its exclusive entry, a command a
    /// cancellation does not cut off, which needs a resolvconf with an exclusive mode, as openresolv's. Then, rather
    /// than let DNS go around the tunnel, it fails when <c>/etc/resolv.conf</c> names no server, or any server but
    /// these and systemd-resolved's stub, <c>127.0.0.53</c> or <c>127.0.0.54</c>.
    /// </para>
    /// <para>
    /// While the servers are set, an nftables rule sends DNS over TCP or UDP port 53 that the host forwards, a
    /// container's, to the VPN's first server of its IP version when it is for a network of the host's primary
    /// interfaces or a server systemd-resolved knows, or refuses it where the VPN has no server of that version. DNS
    /// that comes in by a primary interface, or is for one of its addresses, is left alone, and the host's own lookups
    /// never meet the rule. A network change rewrites it; a failure is logged, as only forwarded DNS depends on it.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Neither <c>resolvectl</c> nor <c>resolvconf</c> took the servers, or <c>/etc/resolv.conf</c> does not use
    /// them.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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
                throw new InvalidOperationException(
                    $"{DnsServersError}\n" +
                    $"resolvectl error: {ex.Message}\n" +
                    $"resolvconf error: {fallbackEx.Message}", fallbackEx);
            }
        }

        // Fail-closed too: systemd-resolved takes the VPN's servers even when programs do not ask it, and a
        // server of the local network in /etc/resolv.conf stays off the tunnel, seeing every name looked up.
        var resolvConf = File.Exists(ResolvConfPath)
            ? await File.ReadAllTextAsync(ResolvConfPath, cancellationToken).Vhc()
            : string.Empty;
        if (!IsResolvConfOnVpnDns(resolvConf, dnsServers))
            throw new InvalidOperationException(
                $"{DnsServersError}\n{ResolvConfPath} does not use them, so DNS would go around the VPN.");

        lock (_dnsRedirectLock)
            _dnsRedirectServers = dnsServers.ToArray();

        ApplyDnsRedirect();
    }

    // Every server in resolv.conf is systemd-resolved's stub or one of the VPN's. A file with none counts as
    // another server, as glibc then asks 127.0.0.1. By address, not by its text: an IPv6 server written another
    // way than .NET writes it is the same server. One that is no address counts as another server.
    internal static bool IsResolvConfOnVpnDns(string resolvConf, IEnumerable<IPAddress> dnsServers)
    {
        var allowed = dnsServers.Concat(ResolvedStubServers).ToHashSet();
        var servers = GetNameservers(resolvConf).ToArray();
        return servers.Length > 0 &&
               servers.All(x => IPAddress.TryParse(x, out var ipAddress) && allowed.Contains(ipAddress));
    }

    private static IEnumerable<string> GetNameservers(string resolvConf)
    {
        return resolvConf.Split('\n')
            .Select(x => x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(x => x.Length >= 2 && x[0] == "nameserver")
            .Select(x => x[1]);
    }

    // Docker gives a container on its default network every server systemd-resolved knows, the local network's too,
    // and a container started before the connect has only that one. Its lookup is forwarded, past systemd-resolved,
    // and the local network stays off the tunnel, so that server saw every name. While connected, such DNS goes to the
    // VPN's server. Nothing else changes: a program on this machine never passes prerouting, and what comes in from the
    // local network or is for this machine is no container's way out, so a DNS server here keeps working. By address:
    // prerouting is before the route, and nft's fib, which could look it up, is missing from some kernels.
    // nft, not iptables, as a client has it from the installer. Only forwarded DNS depends on the rule, so a machine
    // that cannot load it still connects. Never throws: a network change runs it on the OS's thread.
    private void ApplyDnsRedirect()
    {
        lock (_dnsRedirectLock) {
            if (_dnsRedirectServers == null)
                return;

            try {
                ExecuteCommand(LoadNftCommand(CreateDnsRedirectRuleset(_dnsRedirectServers)));
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex,
                    "Could not keep forwarded DNS off the local network, so a container's lookups may reach it. " +
                    "Is nftables installed?");
            }
        }
    }

    private string CreateDnsRedirectRuleset(IReadOnlyCollection<IPAddress> vpnDnsServers)
    {
        var primaryIps = new[] { PrimaryAdapterIpV4, PrimaryAdapterIpV6 }.OfType<IPAddress>().ToArray();
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => IsValidAdapterName(x.Name) &&
                        x.GetIPProperties().UnicastAddresses.Any(y => primaryIps.Contains(y.Address)))
            .ToArray();

        // a link's fe80:: is on every link and nothing forwards to it
        var networks = interfaces
            .SelectMany(x => x.GetIPProperties().UnicastAddresses)
            .Where(x => !x.Address.IsIPv6LinkLocal)
            .Select(x => new IpNetwork(x.Address, x.PrefixLength))
            .ToArray();

        var servers = File.Exists(ResolvedServersPath)
            ? GetNameservers(File.ReadAllText(ResolvedServersPath))
            : [];

        return CreateDnsRedirectRuleset(AdapterName, interfaces.Select(x => x.Name), networks, servers, vpnDnsServers);
    }

    // The old tables go in the same load, so no packet meets neither. A table a family, not one inet table, whose NAT
    // needs Linux 5.2. Each network's prefix is this machine's address on it. A server off those networks stays off
    // the tunnel too, as the routes leave out every private range. NAT cannot change a query's family, and the VPN's
    // servers are often IPv4 alone, so a family with none of them is refused instead, in the forward chain, as a NAT
    // chain cannot: the resolver goes on to its next server at once.
    internal static string CreateDnsRedirectRuleset(string adapterName, IEnumerable<string> localInterfaces,
        IReadOnlyCollection<IpNetwork> localNetworks, IEnumerable<string> localServers,
        IReadOnlyCollection<IPAddress> vpnDnsServers)
    {
        var interfaces = string.Join(", ", localInterfaces.Select(x => $"\"{x}\""));
        var servers = localServers
            .Select(x => IPAddress.TryParse(x, out var ipAddress) ? ipAddress : null)
            .OfType<IPAddress>()
            .Where(x => !IPAddress.IsLoopback(x) && !x.IsIPv6LinkLocal && !vpnDnsServers.Contains(x))
            .Select(x => new IpNetwork(x))
            .ToArray();

        var builder = new StringBuilder(CreateDnsRedirectRemoval(adapterName));
        foreach (var addressFamily in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 }) {
            var ownNetworks = localNetworks.Where(x => x.AddressFamily == addressFamily).ToArray();
            var targets = ownNetworks.Concat(servers.Where(x => x.AddressFamily == addressFamily)).SortAndMerge().ToArray();
            if (targets.Length == 0)
                continue;

            var family = addressFamily == AddressFamily.InterNetwork ? "ip" : "ip6";
            var vpnDnsServer = vpnDnsServers.FirstOrDefault(x => x.AddressFamily == addressFamily);
            var ownAddresses = string.Join(", ", ownNetworks.Select(x => x.Prefix));
            var targetList = string.Join(", ", targets);
            var udpAction = vpnDnsServer != null ? $"dnat to {vpnDnsServer}" : "reject";
            var tcpAction = vpnDnsServer != null ? $"dnat to {vpnDnsServer}" : "reject with tcp reset";

            builder.Append($"table {family} {GetDnsRedirectTable(adapterName)} {{\n");
            builder.Append("  chain dns {\n");
            builder.Append(vpnDnsServer != null
                ? "    type nat hook prerouting priority -100; policy accept;\n"
                : "    type filter hook forward priority 0; policy accept;\n");
            if (interfaces.Length > 0)
                builder.Append($"    iifname {{ {interfaces} }} return\n");
            if (ownAddresses.Length > 0)
                builder.Append($"    {family} daddr {{ {ownAddresses} }} return\n");
            builder.Append($"    {family} daddr {{ {targetList} }} udp dport 53 {udpAction}\n");
            builder.Append($"    {family} daddr {{ {targetList} }} tcp dport 53 {tcpAction}\n");
            builder.Append("  }\n}\n");
        }

        return builder.ToString();
    }

    private static string GetDnsRedirectTable(string adapterName) => $"vpnhood_dns_{adapterName}";

    // A table is made first, as deleting one that is not there would fail the whole load
    private static string CreateDnsRedirectRemoval(string adapterName)
    {
        var table = GetDnsRedirectTable(adapterName);
        return $"table ip {table}\ndelete table ip {table}\ntable ip6 {table}\ndelete table ip6 {table}\n";
    }

    private static string LoadNftCommand(string ruleset) => $"printf '%s' '{ruleset}' | nft -f -";

    private void RemoveDnsRedirect()
    {
        lock (_dnsRedirectLock) {
            if (_dnsRedirectServers == null)
                return;

            _dnsRedirectServers = null;
            TryRemoveDnsRedirect(AdapterName, LogLevel.Warning);
        }
    }

    private static void TryRemoveDnsRedirect(string adapterName, LogLevel failureLevel)
    {
        VhInvoker.TryInvoke($"remove the DNS redirect of {adapterName}", failureLevel, () =>
            ExecuteCommand(
                $"if command -v nft >/dev/null; then {LoadNftCommand(CreateDnsRedirectRemoval(adapterName))}; fi"));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It polls the device's descriptor with no time limit, and polls again when a signal interrupts the poll; a stop's
    /// removal of the device ends it. Before the first open, it returns at once.
    /// </remarks>
    /// <exception cref="PInvokeException">The poll failed.</exception>
    protected override void WaitForTunRead()
    {
        if (_pollFdReads != null)
            WaitForTun(_pollFdReads);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It polls the device's descriptor with no time limit, and polls again when a signal interrupts the poll. Before
    /// the first open, it returns at once.
    /// </remarks>
    /// <exception cref="PInvokeException">The poll failed.</exception>
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

    /// <inheritdoc/>
    /// <returns>
    /// <see langword="true"/> if the device took the packet, also when it took only a part, which a TUN device should
    /// never do, and which is logged and counted as a drop; <see langword="false"/> if it cannot take it now, or the
    /// adapter is closed.
    /// </returns>
    /// <exception cref="PInvokeException">The write failed with another error.</exception>
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

            // true ends the base's tries for it; the drop is told, or the packet would count as sent
            DropPacket(ipPacket);
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

    /// <inheritdoc/>
    /// <returns>The number of bytes read; 0 if no packet is ready, or a signal interrupted the read.</returns>
    /// <exception cref="IOException">The adapter is closed.</exception>
    /// <exception cref="PInvokeException">
    /// The read failed, or read no bytes, as it does once a close has put <c>/dev/null</c> in the device's place.
    /// </exception>
    protected override int ReadPacket(byte[] buffer)
    {
        var fd = _tunAdapterFd;
        if (fd == InvalidFd)
            throw new IOException("TUN adapter is closed.");

        var bytesRead = LinuxAPI.read(fd, buffer, (nuint)buffer.Length);
        if (bytesRead > 0)
            return (int)bytesRead;

        // check for errors
        var errorCode = Marshal.GetLastWin32Error();
        return errorCode switch {
            // No data available; the parent read loop polls (WaitForTunRead) and retries.
            OsConstants.Eagain => 0,
            // Interrupted before any byte was read. Report it as "no data" and let the parent drive the
            // retry (it re-checks liveness and polls) rather than spinning here — EINTR is normal, not an
            // error, so it must not throw or count toward the I/O error threshold.
            OsConstants.Eintr => 0,
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

    /// <summary>
    /// Binds a socket to the interface that holds an address (<c>SO_BINDTODEVICE</c>), not to the address, so the
    /// socket's traffic leaves by that interface rather than the tunnel. It is how
    /// <see cref="TunVpnAdapter.ProtectSocket(Socket)"/> keeps a socket out of the tunnel here.
    /// </summary>
    /// <param name="socket">The socket, not bound yet.</param>
    /// <param name="ipAddress">The host's primary address of the socket's IP version.</param>
    /// <exception cref="InvalidOperationException">No interface holds the address.</exception>
    /// <exception cref="PInvokeException">The kernel refused the option, as without CAP_NET_RAW.</exception>
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

    /// <inheritdoc/>
    /// <remarks>
    /// While the adapter still holds its TUN device, it removes it as <see cref="AdapterRemove"/> does, with its NAT
    /// rules, also when the finalizer calls it, which no stop came before: a masquerade rule left for a deleted
    /// interface can break the host's network.
    /// </remarks>
    protected override void DisposeUnmanaged()
    {
        if (_tunAdapterFd != InvalidFd || _ownTunAdapterFd != InvalidFd)
            AdapterRemove();

        base.DisposeUnmanaged();
    }

    /// <summary>Removes the TUN device of an adapter that was not disposed, as <see cref="DisposeUnmanaged"/> does.</summary>
    /// <remarks>
    /// It runs only once nothing holds the adapter, and a started adapter's reader holds it: stop or dispose an adapter
    /// rather than count on this.
    /// </remarks>
    ~LinuxTunVpnAdapter()
    {
        Dispose(false);
    }
}

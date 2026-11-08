using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.VpnAdapters.Abstractions;

/// <summary>
/// Provides the start, stop, restart, packet I/O and network-change handling of a VPN adapter over a TUN-like
/// interface; a derived class supplies the platform's steps.
/// </summary>
/// <remarks>
/// <para>
/// A start runs the derived class's steps in order: <see cref="AdapterAdd"/>; <see cref="SetSessionName"/> when a
/// name is given; <see cref="AddAddress"/> per configured IP version; <see cref="SetMetric"/> and <see cref="SetMtu"/>
/// when given; <see cref="SetDnsServers"/>; <see cref="AddRoute"/> per network and IP version; <see cref="AddNat"/>;
/// the app filter; then <see cref="AdapterOpen"/>. A stop runs <see cref="AdapterClose"/> and
/// <see cref="AdapterRemove"/>. A stop or disposal during a start cancels the token the steps get; the base checks it
/// before the routes and before opening, and each step should observe it, as a start may be torn down part-way.
/// An owner's start runs its steps under no lock of the base, a restart under its restart lock.
/// </para>
/// <para>
/// Packets are read on a task of the adapter's own and handed to the transport's receivers; sent packets are
/// written one by one. Ten exceptions in a row from reading, or ten from writing, stop the adapter: it then
/// restarts itself with <see cref="VpnAdapterSettings.AutoRestart"/>, or raises <see cref="Failed"/>.
/// </para>
/// <para>
/// When the host's network changes, the adapter restarts if <see cref="RestartAfterNetworkAddressChanged"/>
/// says so, or takes the new primary addresses and raises <see cref="PrimaryNetworkChanged"/>; a new temporary
/// IPv6 address on the same network only refreshes the primary addresses.
/// </para>
/// </remarks>
public abstract class TunVpnAdapter : PacketTransport, IVpnAdapter
{
    private readonly int _maxPacketSendDelayMs;
    private const int MaxIoErrorCount = 10;
    private readonly bool _autoRestart;
    private int _mtu = 0xFFFF;

    // Errors in a row, one count per direction, each its thread's own: the reader succeeds at every quiet poll,
    // which would otherwise hide a write that always fails, and the reverse.
    private int _readErrorCount;
    private int _writeErrorCount;
    private readonly bool _autoMetric;
    private HashSet<IPAddress> _lastPrimaryAdapterAddresses = [];

    private static readonly IpNetwork[] WebDeadNetworks =
        [IpNetwork.Parse("203.0.113.1/24"), IpNetwork.Parse("2001:4860:ffff::1234/48")];

    private readonly Lock _stopLock = new();
    private volatile bool _isStarting;
    private bool _isStopping;
    private VpnAdapterOptions? _startOptions;

    // The owner wants the adapter started: set by its Start, cleared by its Stop and Dispose. A restart
    // and a retry start nothing without it, and its source cancels the ones under way. The source is
    // replaced per start and never disposed: it has no timer, and a token in flight may still read it.
    private volatile bool _keepStarted;
    private CancellationTokenSource _keepStartedCts = new();

    // This run's reader, which a stop may wait for (WaitForReader), and the run it reads for, by its task's
    // id: a stop retires it, so a reader that outlives the stop leaves instead of reading the next run's
    // session. The sender is waited for by the transport's own sending flag (WaitForSender), so nothing
    // is added per batch; only the adapter whose sender is stopping it on its own errors is marked, on
    // that thread.
    private volatile Task _readerTask = Task.CompletedTask;
    private volatile int _readerRunId;
    private volatile int _readerThreadId;
    [ThreadStatic] private static TunVpnAdapter? _senderErrorStopAdapter;

    /// <summary>
    /// Gets the VPN server's address the current run's options name, or <see langword="null"/>.
    /// </summary>
    protected IPAddress? ServerIp => _startOptions?.ServerIp;

    /// <summary>
    /// Gets a value indicating whether the current run translates its virtual networks (NAT).
    /// </summary>
    // ReSharper disable once FieldCanBeMadeReadOnly.Local
    protected bool UseNat { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the adapter can include or exclude apps from the tunnel
    /// (<see cref="VpnAdapterOptions.IncludeApps"/>, <see cref="VpnAdapterOptions.ExcludeApps"/>).
    /// </summary>
    public abstract bool IsAppFilterSupported { get; }

    /// <inheritdoc/>
    public abstract bool IsNatSupported(IpVersion ipVersion);

    /// <summary>
    /// Gets a value indicating whether the adapter can keep a socket's traffic out of the tunnel. The default is
    /// <see langword="true"/>.
    /// </summary>
    public virtual bool CanProtectSocket => true;

    /// <summary>
    /// Gets a value indicating whether the adapter keeps a socket out of the tunnel by binding it to the primary
    /// address, rather than by an OS call of its own.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, the routes leave out the probe networks the adapter aims at to find the primary
    /// addresses, and that search runs on an unprotected socket after a network change.
    /// </remarks>
    protected abstract bool IsSocketProtectedByBind { get; }

    /// <summary>
    /// Gets the ID the app filter knows this app by, which it always lets into the tunnel, or
    /// <see langword="null"/> where there is no app filter.
    /// </summary>
    protected abstract string? AppPackageId { get; }

    /// <summary>
    /// Sets the interface's MTU. A start calls it when <see cref="VpnAdapterOptions.Mtu"/> is set.
    /// </summary>
    /// <param name="mtu">The MTU in bytes.</param>
    /// <param name="ipV4"><see langword="true"/> if the interface carries IPv4 in this run.</param>
    /// <param name="ipV6"><see langword="true"/> if the interface carries IPv6 in this run.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the interface's route metric. A start calls it when <see cref="VpnAdapterOptions.Metric"/> is set.
    /// </summary>
    /// <param name="metric">The metric.</param>
    /// <param name="ipV4"><see langword="true"/> if the interface carries IPv4 in this run.</param>
    /// <param name="ipV6"><see langword="true"/> if the interface carries IPv6 in this run.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the DNS servers the interface gives the host. A start calls it every time.
    /// </summary>
    /// <param name="dnsServers">The servers, only of the IP versions the interface carries; may be empty.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers, CancellationToken cancellationToken);

    /// <summary>
    /// Routes a network through the interface. A start calls it once per network, for each IP version the
    /// interface carries.
    /// </summary>
    /// <param name="ipNetwork">The network.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the interface its address in a virtual network. A start calls it once per configured IP version.
    /// </summary>
    /// <param name="ipNetwork">The interface's address with its network.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken);

    /// <summary>
    /// Translates a virtual network to the host's primary address (NAT). A start calls it with
    /// <see cref="VpnAdapterOptions.UseNat"/>, for each IP version the adapter's NAT translates and the host has
    /// a primary address of.
    /// </summary>
    /// <param name="ipNetwork">The virtual network.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken);

    /// <summary>
    /// Names the session where the OS shows it. A start calls it when <see cref="VpnAdapterOptions.SessionName"/>
    /// is not empty.
    /// </summary>
    /// <param name="sessionName">The name.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task SetSessionName(string sessionName, CancellationToken cancellationToken);

    /// <summary>
    /// Lets only these apps' traffic into the tunnel. A start calls it with
    /// <see cref="VpnAdapterOptions.IncludeApps"/>, where <see cref="IsAppFilterSupported"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="packageIds">The apps' IDs, this app's own included.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps these apps' traffic out of the tunnel. A start calls it with
    /// <see cref="VpnAdapterOptions.ExcludeApps"/>, where <see cref="IsAppFilterSupported"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="packageIds">The apps' IDs, never this app's own.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the interface: the first step of a start.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task AdapterAdd(CancellationToken cancellationToken);

    /// <summary>
    /// Removes the interface and what the start added with it: the last step of a stop, after
    /// <see cref="AdapterClose"/>. When the base calls it, it runs under the base's stop lock and must not wait for
    /// the reader or a sender to take that lock. It may run after a partial start.
    /// </summary>
    protected abstract void AdapterRemove();

    /// <summary>
    /// Opens the interface for reading and writing packets: the last step of a start, before the reader starts.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract Task AdapterOpen(CancellationToken cancellationToken);

    /// <summary>
    /// Closes the interface for reading and writing: the first step of a stop, under the base's stop lock. It may run
    /// after a partial start.
    /// </summary>
    protected abstract void AdapterClose();

    /// <summary>
    /// Blocks until the interface may take a packet again, between the retries of a write it refused.
    /// </summary>
    protected abstract void WaitForTunWrite();

    /// <summary>
    /// Blocks until a packet may be ready, after a read found none. It runs on the reader's task.
    /// </summary>
    protected abstract void WaitForTunRead();

    /// <summary>
    /// Gets a value indicating whether the adapter restarts when the host moves to other networks. When
    /// <see langword="false"/>, it takes the new primary addresses and raises <see cref="PrimaryNetworkChanged"/>.
    /// </summary>
    protected abstract bool RestartAfterNetworkAddressChanged { get; }

    /// <summary>
    /// Gets a value indicating whether the adapter checks, at each change of the host's network, that its
    /// interface is still there, and stops when it is not. The check is <see cref="IsAdapterInterfaceEnabled"/>,
    /// which by default looks for the interface by name or address only.
    /// </summary>
    // an interface of the adapter's own, which a user can disable or set down: no I/O error tells of it,
    // only the OS's network events (CheckAdapterInterface)
    protected abstract bool CheckAdapterInterfaceAfterNetworkChanged { get; }

    /// <summary>
    /// Gets the wait before <see cref="VpnAdapterSettings.AutoRestart"/> tries a failed restart again. The
    /// default is 10 seconds.
    /// </summary>
    protected virtual TimeSpan AutoRestartDelay => TimeSpan.FromSeconds(10);

    /// <summary>
    /// Reads one packet from the interface into a buffer, without waiting for one.
    /// </summary>
    /// <param name="buffer">The buffer, at least the MTU long.</param>
    /// <returns><see langword="true"/> if a packet was read; <see langword="false"/> if none is ready.</returns>
    protected abstract bool ReadPacket(byte[] buffer);

    /// <summary>
    /// Writes one packet to the interface, without waiting for room.
    /// </summary>
    /// <param name="ipPacket">The packet.</param>
    /// <returns>
    /// <see langword="true"/> if the interface took it; <see langword="false"/> if it cannot take it now, which the
    /// adapter retries with doubling waits (see <see cref="VpnAdapterSettings.MaxPacketSendDelay"/>).
    /// </returns>
    protected abstract bool WritePacket(IpPacket ipPacket);

    /// <inheritdoc/>
    public event EventHandler? Disposed;

    /// <inheritdoc/>
    public event EventHandler<Exception>? Failed;

    /// <inheritdoc/>
    public event EventHandler<VpnAdapterOptions>? Started;

    /// <summary>
    /// Gets the name of the adapter's interface.
    /// </summary>
    public string AdapterName { get; }

    /// <summary>
    /// Gets the host's primary IPv4 address, outside the tunnel, or <see langword="null"/>. A stop clears it; a
    /// start finds it again.
    /// </summary>
    public IPAddress? PrimaryAdapterIpV4 { get; private set; } = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);

    /// <summary>
    /// Gets the host's primary IPv6 address, outside the tunnel, or <see langword="null"/>. A stop clears it; a
    /// start finds it again.
    /// </summary>
    public IPAddress? PrimaryAdapterIpV6 { get; private set; } = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);

    /// <summary>
    /// Gets the interface's IPv4 address with its network in the current run, or <see langword="null"/>.
    /// </summary>
    public IpNetwork? AdapterIpNetworkV4 { get; private set; }

    /// <summary>
    /// Gets the interface's IPv6 address with its network in the current run, or <see langword="null"/>.
    /// </summary>
    public IpNetwork? AdapterIpNetworkV6 { get; private set; }

    /// <summary>
    /// Gets the gateway of the interface's IPv4 network: its first address plus one, or <see langword="null"/>
    /// for a /31 or /32 network or when not started.
    /// </summary>
    public IPAddress? GatewayIpV4 { get; private set; }

    /// <summary>
    /// Gets the gateway of the interface's IPv6 network: its first address plus one, or <see langword="null"/>
    /// for a /128 network or when not started. A run whose IPv6 address could not be added may still show it.
    /// </summary>
    public IPAddress? GatewayIpV6 { get; private set; }

    /// <inheritdoc/>
    public virtual bool IsIpVersionSupported(IpVersion ipVersion) => GetPrimaryAdapterAddress(ipVersion) != null;

    /// <inheritdoc/>
    public bool IsStarted { get; private set; }

    /// <inheritdoc/>
    public event EventHandler? PrimaryAdapterIpChanged;

    /// <inheritdoc/>
    public event EventHandler? PrimaryNetworkChanged;

    // ReSharper disable once InconsistentlySynchronizedField
    private bool IsReady => IsStarted && !_isStopping && !IsDisposed && !IsDisposing;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunVpnAdapter"/> class, which starts watching the host's
    /// network changes until it is disposed.
    /// </summary>
    /// <param name="adapterSettings">The adapter's settings.</param>
    /// <exception cref="ArgumentOutOfRangeException">The settings' blocking timeout is negative or too long.</exception>
    protected TunVpnAdapter(VpnAdapterSettings adapterSettings)
        : base(adapterSettings)
    {
        _maxPacketSendDelayMs = (int)adapterSettings.MaxPacketSendDelay.TotalMilliseconds;
        _autoRestart = adapterSettings.AutoRestart;
        _autoMetric = adapterSettings.AutoMetric;
        AdapterName = adapterSettings.AdapterName;
        NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
    }

    /// <inheritdoc/>
    public IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? PrimaryAdapterIpV4 : PrimaryAdapterIpV6;
    }

    /// <summary>
    /// Gets the gateway of the interface's network of an IP version.
    /// </summary>
    /// <param name="ipVersion">The IP version.</param>
    /// <returns><see cref="GatewayIpV4"/> or <see cref="GatewayIpV6"/>, which may be <see langword="null"/>.</returns>
    public IPAddress? GetGatewayIp(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? GatewayIpV4 : GatewayIpV6;
    }

    /// <summary>
    /// Gets the interface's address with its network of an IP version.
    /// </summary>
    /// <param name="ipVersion">The IP version.</param>
    /// <returns><see cref="AdapterIpNetworkV4"/> or <see cref="AdapterIpNetworkV6"/>, which may be <see langword="null"/>.</returns>
    public IpNetwork? GetIpNetwork(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? AdapterIpNetworkV4 : AdapterIpNetworkV6;
    }

    /// <summary>
    /// Starts the adapter: makes its interface with its addresses, DNS servers, routes, NAT and app filter, opens
    /// it and starts reading packets.
    /// </summary>
    /// <remarks>
    /// Await one start before the next. From here the adapter keeps itself started until <see cref="Stop()"/>,
    /// disposal, or a failure it does not recover from (without <see cref="VpnAdapterSettings.AutoRestart"/>).
    /// <see cref="IsStarted"/> is <see langword="true"/> from the start's beginning, before the interface exists. A
    /// failed start tries to remove what it added and leaves the adapter stopped; it raises no
    /// <see cref="Failed"/>, as the caller has the exception. An IPv6 address the interface refuses is logged, and
    /// the run goes on without IPv6. Before it completes, it raises <see cref="Started"/> on its own thread.
    /// </remarks>
    /// <param name="options">The options this run uses, which each restart uses again.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. It completes when the adapter is up.</returns>
    /// <exception cref="ObjectDisposedException">The adapter is disposed.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or a stop or disposal came during the start.
    /// </exception>
    /// <exception cref="NotSupportedException"><see cref="VpnAdapterOptions.UseNat"/> is set and the adapter's NAT does not translate IPv4.</exception>
    /// <exception cref="InvalidOperationException">
    /// Both <see cref="VpnAdapterOptions.IncludeApps"/> and <see cref="VpnAdapterOptions.ExcludeApps"/> are non-empty
    /// on an adapter with an app filter, or the filter has no <see cref="AppPackageId"/>.
    /// </exception>
    /// <exception cref="Exception">A route could not be added; the message names it.</exception>
    // The owner awaits a start before the next: one a stop overtook stops the adapter as it fails, and so a
    // start made meanwhile
    public async Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed || IsDisposing, this);

        // the owner's start: from here the adapter keeps itself started, until the owner's Stop or Dispose
        _keepStarted = true;
        var keepStartedCts = new CancellationTokenSource();
        _keepStartedCts = keepStartedCts;
        try {
            // a Stop or a Dispose meanwhile ends the start at its next step, rather than letting it add the
            // rest - a tun, routes, DNS - after the stop, until its end finds it stopped
            using var startCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, keepStartedCts.Token);
            await StartCore(options, startCts.Token).Vhc();
        }
        catch {
            _keepStarted = false; // the caller has the exception: nothing to keep started
            throw;
        }

        // an address change or a disable during the start was left to it: see them now
        CheckPrimaryAdapterAddresses();
        await CheckAdapterInterface().Vhc();
    }

    // The start itself, for the owner's Start and for a restart
    private async Task StartCore(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        _startOptions = options;

        if (options.UseNat && !IsNatSupported(IpVersion.IPv4))
            throw new NotSupportedException("NAT is not supported by this adapter.");

        var wasStarted = false; // this start marked the adapter started
        try {
            VhLogger.Instance.LogInformation("Starting the VPN adapter. AdapterName: {AdapterName}", AdapterName);

            // We must set the started at first, to let clean-up be done stop via any exception.
            // We hope client await the start otherwise we need different state for adapters
            _isStarting = true;
            lock (_stopLock) {
                // a disposal or a stop that got here first found nothing to stop, so nothing may start after it;
                // a stop that cleared the flag before this start set it may still cancel this start's token
                ObjectDisposedException.ThrowIf(IsDisposed || IsDisposing, this);
                if (!_keepStarted || _keepStartedCts.IsCancellationRequested)
                    throw new OperationCanceledException("The VPN adapter has been stopped while starting.");
                IsStarted = true;
                wasStarted = true;
            }
            // a new run, not the errors that ended the last one
            _readErrorCount = 0;
            _writeErrorCount = 0;

            // A concurrent Stop() clears the adapter properties when the user disconnects mid-start,
            // so the rest of this method must read these locals, never the properties.
            var adapterIpNetworkV4 = options.VirtualIpNetworkV4;
            var adapterIpNetworkV6 = options.VirtualIpNetworkV6;
            AdapterIpNetworkV4 = adapterIpNetworkV4;
            AdapterIpNetworkV6 = adapterIpNetworkV6;
            UseNat = options.UseNat;
            _mtu = options.Mtu ?? _mtu;

            // the host's addresses before the primary IPs, so a change between the two is one the start's look at
            // its end sees; after the virtual networks, by which the record leaves our own adapter out
            _lastPrimaryAdapterAddresses = GetPrimaryAdapterAddresses();
            var primaryAdapterIpV4 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetwork);
            var primaryAdapterIpV6 = DiscoverPrimaryAdapterIp(AddressFamily.InterNetworkV6);
            PrimaryAdapterIpV4 = primaryAdapterIpV4;
            PrimaryAdapterIpV6 = primaryAdapterIpV6;

            // report the primary adapter IPs
            VhLogger.Instance.LogInformation(
                "TunAdapterInfo. AdapterType: {AdapterType}, UseNat: {UseNat}, MTU: {MTU}, " +
                "PrimaryAdapterIpV4: {PrimaryAdapterIpV4}, PrimaryAdapterIpV6: {PrimaryAdapterIpV6}, " +
                "AdapterIpNetworkV4: {AdapterIpNetworkV4}, AdapterIpNetworkV6: {AdapterIpNetworkV6}",
                VhLogger.FormatType(this), UseNat, _mtu,
                VhLogger.Format(primaryAdapterIpV4), VhLogger.Format(primaryAdapterIpV6),
                adapterIpNetworkV4, adapterIpNetworkV6);

            // create tun adapter
            VhLogger.Instance.LogInformation("Adding TUN adapter...");
            await AdapterAdd(cancellationToken).Vhc();

            // SetSessionName
            if (!string.IsNullOrEmpty(options.SessionName))
                await SetSessionName(options.SessionName, cancellationToken).Vhc();

            // Set adapter IPv4 address
            if (adapterIpNetworkV4 != null) {
                VhLogger.Instance.LogDebug("Adding IPv4 address to adapter ...");
                GatewayIpV4 = BuildGatewayFromFromNetwork(adapterIpNetworkV4);
                await AddAddress(adapterIpNetworkV4, cancellationToken).Vhc();
            }

            // Set adapter IPv6 address
            if (adapterIpNetworkV6 != null) {
                VhLogger.Instance.LogDebug("Adding IPv6 address to adapter ...");
                try {
                    GatewayIpV6 = BuildGatewayFromFromNetwork(adapterIpNetworkV6);
                    await AddAddress(adapterIpNetworkV6, cancellationToken).Vhc();
                }
                // a cancellation is no IPv6 failure: it passes to the start's own catch, which stops the adapter
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogError(ex,
                        "Failed to add IPv6 address to TUN adapter. AdapterIpNetworkV6: {AdapterIpNetworkV6}",
                        adapterIpNetworkV6);
                    adapterIpNetworkV6 = null;
                    AdapterIpNetworkV6 = null;
                }
            }

            // set metric
            if (options.Metric != null) {
                VhLogger.Instance.LogDebug("Setting metric...");
                await SetMetric(options.Metric.Value,
                    ipV4: adapterIpNetworkV4 != null,
                    ipV6: adapterIpNetworkV6 != null,
                    cancellationToken).Vhc();
            }

            // set mtu
            if (options.Mtu != null) {
                VhLogger.Instance.LogDebug("Setting MTU...");
                await SetMtu(options.Mtu.Value,
                    ipV4: adapterIpNetworkV4 != null,
                    ipV6: adapterIpNetworkV6 != null,
                    cancellationToken).Vhc();
            }

            // set DNS servers
            VhLogger.Instance.LogDebug("Setting DNS servers...");
            var dnsServers = options.DnsServers;
            if (adapterIpNetworkV4 == null)
                dnsServers = dnsServers.Where(x => !x.IsV4()).ToArray();
            if (adapterIpNetworkV6 == null)
                dnsServers = dnsServers.Where(x => !x.IsV6()).ToArray();
            await SetDnsServers(dnsServers, cancellationToken).Vhc();

            // exclude dead networks
            var includeNetworks = options.IncludeNetworks.ToArray();
            if (IsSocketProtectedByBind) {
                includeNetworks = includeNetworks
                    .ToIpRanges()
                    .Exclude(WebDeadNetworks.ToIpRanges())
                    .ToIpNetworks()
                    .ToArray();
            }

            // add routes; not after a stop, which a step that ignores the token (WinTun's, Android's) ran past
            cancellationToken.ThrowIfCancellationRequested();
            VhLogger.Instance.LogDebug("Adding routes...");
            if (adapterIpNetworkV4 != null)
                await AddRouteHelper(includeNetworks, AddressFamily.InterNetwork, cancellationToken).Vhc();
            if (adapterIpNetworkV6 != null)
                await AddRouteHelper(includeNetworks, AddressFamily.InterNetworkV6, cancellationToken).Vhc();

            // add NAT, for each IP version the adapter's NAT translates
            if (UseNat) {
                VhLogger.Instance.LogDebug("Adding NAT...");
                if (adapterIpNetworkV4 != null && primaryAdapterIpV4 != null && IsNatSupported(IpVersion.IPv4))
                    await AddNat(adapterIpNetworkV4, cancellationToken).Vhc();

                if (adapterIpNetworkV6 != null && primaryAdapterIpV6 != null && IsNatSupported(IpVersion.IPv6))
                    await AddNat(adapterIpNetworkV6, cancellationToken).Vhc();
            }

            // add app filter
            if (IsAppFilterSupported)
                await SetAppFilters(options.IncludeApps, options.ExcludeApps, cancellationToken).Vhc();

            // open the adapter; not after a stop, as the routes
            cancellationToken.ThrowIfCancellationRequested();
            VhLogger.Instance.LogInformation("Opening TUN adapter...");
            await AdapterOpen(cancellationToken).Vhc();

            // start reading packets; the task is published before it runs, so a stop from here waits
            // for this run's reader, not the last one's. Under the stop lock: a stop that overtook this
            // start has retired its run, and no reader may start after it. On the default scheduler, as
            // Task.Run: never on a caller's own scheduler, which the blocking reader would hold
            var readerTask = new Task(RunReader, TaskCreationOptions.DenyChildAttach);
            lock (_stopLock) {
                if (!IsStarted)
                    throw new OperationCanceledException("The VPN adapter has been stopped while starting.");

                _readerThreadId = 0;
                _readerTask = readerTask;
                _readerRunId = readerTask.Id;
                readerTask.Start(TaskScheduler.Default);
            }

            VhLogger.Instance.LogInformation("TUN adapter started.");

            // a failed handler must not fail the start
            VhInvoker.TryInvoke("raise the adapter's Started event", LogLevel.Error,
                () => Started?.Invoke(this, options));
        }
        catch (Exception ex) {
            // when a concurrent Stop() tears the adapter down mid-start, or the start is cancelled (a
            // terminal's Ctrl+C also kills the command it runs), the first step to fail is fallout of
            // that, so report the start as canceled rather than broken
            var isStopped = _isStopping || !IsStarted || cancellationToken.IsCancellationRequested;
            VhLogger.Instance.Log(ex is OperationCanceledException || isStopped ? LogLevel.Trace : LogLevel.Error,
                ex, "Failed to start TUN adapter.");

            // a stop that overtook this start removed only what it found; what the start added since goes
            // here, as no later start may come to clear it (a dispose)
            if (!Stop(false) && wasStarted)
                RemoveAfterOvertakingStop();

            if (isStopped && ex is not OperationCanceledException)
                throw new OperationCanceledException("The VPN adapter has been stopped while starting.", ex);
            throw;
        }
        finally {
            _isStarting = false;
        }
    }

    private async Task AddRouteHelper(IEnumerable<IpNetwork> ipNetworks, AddressFamily addressFamily,
        CancellationToken cancellationToken)
    {
        // remove the local networks
        ipNetworks = ipNetworks.Where(x => x.AddressFamily == addressFamily);

        if (_autoMetric) {
            // ReSharper disable once PossibleMultipleEnumeration
            var sortedIpNetworks = ipNetworks.Sort();

            // ReSharper disable once PossibleMultipleEnumeration
            if (addressFamily.IsV4() && sortedIpNetworks.IsAllV4())
                ipNetworks = VpnAdapterOptions.AllVRoutesIpV4;

            // ReSharper disable once PossibleMultipleEnumeration
            if (addressFamily.IsV6() && sortedIpNetworks.IsAllV6())
                ipNetworks = VpnAdapterOptions.AllVRoutesIpV6;
        }

        // ReSharper disable once PossibleMultipleEnumeration
        foreach (var network in ipNetworks) {
            try {
                await AddRoute(network, cancellationToken).Vhc();
            }
            catch (Exception ex) {
                throw new Exception($"Could not add {network} to route. {ex.Message}");
            }
        }
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    private async Task SetAppFilters(IEnumerable<string>? includeApps, IEnumerable<string>? excludeApps, CancellationToken cancellationToken)
    {
        var appPackageId = AppPackageId;

        // validate the app filter
        if (appPackageId == null)
            throw new InvalidOperationException("AppPackageId must be available when AppFilter is supported.");

        if (!VhUtils.IsNullOrEmpty(includeApps) && !VhUtils.IsNullOrEmpty(excludeApps))
            throw new InvalidOperationException("Both include and exclude apps cannot be set at the same time.");

        // make sure current app is in the allowed list
        if (includeApps != null) {
            includeApps = includeApps.Concat([appPackageId]).Distinct();
            await SetAllowedApps(includeApps, cancellationToken).Vhc();
        }

        // make sure current app is not in the disallowed list
        if (excludeApps != null) {
            excludeApps = excludeApps.Where(x => x != appPackageId).Distinct();
            await SetDisallowedApps(excludeApps, cancellationToken).Vhc();
        }
    }

    /// <summary>
    /// Stops the adapter and removes its interface, with its routes and DNS servers. A restart under way ends
    /// first, and a pending retry is dropped, even when the adapter is already stopped.
    /// </summary>
    /// <remarks>
    /// The adapter counts as stopped even when closing or removing the interface fails: the error reaches the caller,
    /// and the next start removes the leftover.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The adapter is disposed.</exception>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // first: a restart under way starts nothing after its stop, and a pending retry ends
        _keepStarted = false;
        _keepStartedCts.TryCancel();
        using var restartLock = WaitForRestart();
        Stop(throwException: true);
    }

    // Returns whether this call stopped it: not when it was not started, or another call is stopping it.
    // The call that passes the guard owns the stop: the adapter counts as stopped whatever its teardown
    // did, as the next start removes a leftover, and a stop that went unnoticed would be the worse.
    private bool Stop(bool throwException)
    {
        lock (_stopLock) {
            if (!IsStarted || _isStopping)
                return false;

            VhLogger.Instance.LogInformation("Stopping {AdapterName} adapter.", AdapterName);
            _isStopping = true;
            _readerRunId = 0; // this run's reader leaves, even one that outlives the stop's wait
            try {
                AdapterClose();
                AdapterRemove();
                VhLogger.Instance.LogInformation("TUN adapter stopped.");
            }
            catch (Exception ex) when (!throwException) {
                VhLogger.Instance.LogError(ex, "Failed to stop the TUN adapter. AdapterName: {AdapterName}",
                    AdapterName);
            }
            finally {
                PrimaryAdapterIpV4 = null;
                PrimaryAdapterIpV6 = null;
                AdapterIpNetworkV4 = null;
                AdapterIpNetworkV6 = null;
                GatewayIpV4 = null;
                GatewayIpV6 = null;
                IsStarted = false;
                _isStopping = false;
            }

            return true;
        }
    }

    // Under the stop lock, and not once a new run has started: what is there is then that run's
    private void RemoveAfterOvertakingStop()
    {
        lock (_stopLock) {
            if (IsStarted)
                return;

            VhLogger.Instance.LogInformation(
                "Removing what the start added after a stop overtook it. AdapterName: {AdapterName}", AdapterName);
            try {
                AdapterClose();
                AdapterRemove();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex,
                    "Could not remove what the start added after a stop overtook it. AdapterName: {AdapterName}",
                    AdapterName);
            }
        }
    }

    /// <summary>
    /// Binds a socket to an address, which is how <see cref="ProtectSocket(Socket)"/> keeps it out of the tunnel.
    /// The default binds it to the address with any port.
    /// </summary>
    /// <param name="socket">The socket, not bound yet.</param>
    /// <param name="address">The host's primary address of the socket's IP version.</param>
    protected virtual void BindSocketToIp(Socket socket, IPAddress address)
    {
        socket.Bind(new IPEndPoint(address, 0));
    }

    /// <summary>
    /// Keeps a socket's traffic out of the tunnel by binding it to the host's primary address of its IP version.
    /// Call it before the socket is bound or connected.
    /// </summary>
    /// <param name="socket">The socket, not bound yet.</param>
    /// <returns>
    /// <see langword="true"/> if the socket is bound; <see langword="false"/> if the host has no primary address of
    /// the socket's IP version.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The adapter is disposed.</exception>
    /// <exception cref="InvalidOperationException">The socket is already bound.</exception>
    /// <exception cref="NotSupportedException">The socket is neither IPv4 nor IPv6.</exception>
    public virtual bool ProtectSocket(Socket socket)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (socket.LocalEndPoint != null)
            throw new InvalidOperationException("Could not protect an already bound socket.");

        // get the primary adapter IP
        var primaryAdapterIp = GetPrimaryAdapterAddress(socket.AddressFamily.IpVersion());
        if (primaryAdapterIp == null)   
            return false;

        // bind the socket to the primary adapter IP
        BindSocketToIp(socket, primaryAdapterIp);
        return true;
    }

    /// <summary>
    /// Keeps a socket's traffic to a given address out of the tunnel by binding it to the host's primary address
    /// of its IP version. Call it before the socket is bound or connected.
    /// </summary>
    /// <param name="socket">The socket, not bound yet.</param>
    /// <param name="remoteAddress">The remote address the socket will reach.</param>
    /// <returns>
    /// <see langword="true"/> if the socket is bound; <see langword="false"/> if the host has no primary address of
    /// the socket's IP version, or exactly one of the two addresses is a loopback address, which no bind can
    /// route.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The adapter is disposed.</exception>
    /// <exception cref="InvalidOperationException">The socket is already bound.</exception>
    /// <exception cref="NotSupportedException">The socket is neither IPv4 nor IPv6.</exception>
    public virtual bool ProtectSocket(Socket socket, IPAddress remoteAddress)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (socket.LocalEndPoint != null)
            throw new InvalidOperationException("Could not protect an already bound socket.");

        // get the primary adapter IP
        var primaryAdapterIp = GetPrimaryAdapterAddress(socket.AddressFamily.IpVersion());
        if (primaryAdapterIp == null) 
            return false;

        // could not protect loopback addresses or not needed at all, because loopback can not be routed
        if (IPAddress.IsLoopback(primaryAdapterIp) != IPAddress.IsLoopback(remoteAddress)) 
            return false;

        // bind the socket to the primary adapter IP and connect to the remote endpoint
        BindSocketToIp(socket, primaryAdapterIp);
        return true;
    }

    /// <summary>
    /// Gets the DNS servers of the network the tunnel runs over: the host's DNS servers as .NET lists them, less
    /// those the tunnel itself gives.
    /// </summary>
    /// <returns>The DNS servers; none where .NET lists none, which a platform's adapter may answer itself.</returns>
    // The machine's DNS servers as .NET lists them, but this tunnel's own: Linux lists them among systemd-resolved's
    // links. A platform where .NET lists none, Android, has its own.
    public virtual IReadOnlyList<IPAddress> GetPrimaryAdapterDnsServers()
    {
        var tunnelDnsServers = _startOptions?.DnsServers ?? [];
        return DnsUtils.GetNetworkDnsServers()
            .Where(x => !tunnelDnsServers.Contains(x))
            .ToArray();
    }

    private void DiscoverPrimaryAdapterIps(bool protect)
    {
        var primaryAdapterIpV4 = DiscoverPrimaryAdapterIpViaProtect(AddressFamily.InterNetwork, protect);
        var primaryAdapterIpV6 = DiscoverPrimaryAdapterIpViaProtect(AddressFamily.InterNetworkV6, protect);

        // If the primary adapter IPs are changed, update them and notify subscribers
        if (!Equals(primaryAdapterIpV4, PrimaryAdapterIpV4) || !Equals(primaryAdapterIpV6, PrimaryAdapterIpV6)) {
            PrimaryAdapterIpV4 = primaryAdapterIpV4;
            PrimaryAdapterIpV6 = primaryAdapterIpV6;
            PrimaryAdapterIpChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IPAddress? DiscoverPrimaryAdapterIpViaProtect(AddressFamily addressFamily, bool protect)
    {
        using var udpClient = new UdpClient(addressFamily);
        if (protect) {
            try {
                var ipAddress = WebDeadNetworks.First(x => x.AddressFamily == addressFamily).Prefix;
                ProtectSocket(udpClient.Client, ipAddress);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to protect the socket for discovering primary adapter IP. AddressFamily: {AddressFamily}",
                    addressFamily);
                return null;
            }
        }
        return DiscoverPrimaryAdapterIp(udpClient);
    }

    private static IPAddress? DiscoverPrimaryAdapterIp(AddressFamily addressFamily)
    {
        using var udpClient = new UdpClient(addressFamily);
        return DiscoverPrimaryAdapterIp(udpClient);
    }

    private static IPAddress? DiscoverPrimaryAdapterIp(UdpClient protectedUdpClient)
    {
        var addressFamily = protectedUdpClient.Client.AddressFamily;
        // not matter is it reachable or not, just try to get the primary adapter IP which can route to the internet
        var ipAddress = WebDeadNetworks.First(x => x.AddressFamily == addressFamily).Prefix;
        var remoteEndPoint = new IPEndPoint(ipAddress, 53);

        try {
            // IPv6 needs the addressFamily to be set
            protectedUdpClient.Connect(remoteEndPoint);
            var localEndPoint = protectedUdpClient.Client.GetLocalEndPoint();

            // in IPV6, the local ip may be some random ip for another virtual adapter.
            // valid one should be global unicast and assigned to local interfaces
            if (addressFamily.IsV6() && !IpNetwork.AllGlobalUnicastV6.Contains(localEndPoint.Address))
                throw new Exception("The discovered primary adapter IP is not assigned to any local interface.");

            // log the discovered primary adapter IP
            VhLogger.Instance.LogDebug(
                "Primary adapter IP discovered. PrimaryAdapterIp: {PrimaryAdapterIp}",
                VhLogger.Format(localEndPoint.Address));

            return localEndPoint.Address;
        }
        catch (Exception) {
            VhLogger.Instance.LogDebug("Failed to get primary adapter IP. RemoteEndPoint: {RemoteEndPoint}",
                remoteEndPoint);
            return null;
        }
    }

    private static IPAddress? BuildGatewayFromFromNetwork(IpNetwork ipNetwork)
    {
        // Check for small subnets (IPv4: /31, /32 | IPv6: /128)
        return ipNetwork is { IsV4: true, PrefixLength: >= 31 } or { IsV6: true, PrefixLength: 128 }
            ? null
            : IPAddressUtil.Increment(ipNetwork.FirstIpAddress);
    }

    /// <summary>
    /// Writes a batch of packets to the interface, one after another, with <see cref="SendPacket"/>.
    /// </summary>
    /// <param name="ipPackets">The packets.</param>
    /// <returns>A completed task: the writes are synchronous.</returns>
    /// <exception cref="InvalidOperationException">The adapter is not started, or is stopping or disposed.</exception>
    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++)
            SendPacket(ipPackets[i]);

        return default;
    }

    /// <summary>
    /// Writes one packet to the interface, retrying with doubling waits while it cannot take it (see
    /// <see cref="VpnAdapterSettings.MaxPacketSendDelay"/>), after which the packet is dropped with a warning.
    /// </summary>
    /// <remarks>
    /// A packet the interface refuses during a stop or a start is dropped. Ten exceptions in a row from writing stop
    /// the adapter, which then restarts itself or raises <see cref="Failed"/>; a send that completes without an
    /// exception, a drop included, resets the count.
    /// </remarks>
    /// <param name="ipPacket">The packet.</param>
    /// <exception cref="InvalidOperationException">The adapter is not started, or is stopping or disposed.</exception>
    protected void SendPacket(IpPacket ipPacket)
    {
        if (!IsReady)
            throw new InvalidOperationException("TUN adapter is not in ready state.");

        try {
            SendPacketInternal(ipPacket);
            _writeErrorCount = 0;
        }
        catch (Exception ex) {
            _writeErrorCount++;
            if (_writeErrorCount < MaxIoErrorCount)
                throw;

            // too many errors in a row: the adapter is broken, so stop it, and recover or tell the owner
            if (StopBySender())
                OnUnrequestedStop(ex);
            throw;
        }
    }

    // the stop runs inside the sender's batch, so it must not wait for that batch; the previous mark is
    // restored, as this stop may run inside another adapter's. The sender's errors are the current run's.
    private bool StopBySender()
    {
        var previous = _senderErrorStopAdapter;
        _senderErrorStopAdapter = this;
        try {
            return StopOnIoErrors(_readerRunId);
        }
        finally {
            _senderErrorStopAdapter = previous;
        }
    }

    // The reader's or the sender's stop on its own I/O errors never waits for the stop lock: a stop that
    // holds it may be waiting for that very thread, and the teardown is that stop's anyway. It stops only
    // the run the errors came from, checked under the lock: a stop and a restart may have replaced it.
    private bool StopOnIoErrors(int runId)
    {
        if (!_stopLock.TryEnter())
            return false;

        try {
            return _readerRunId == runId && Stop(false);
        }
        finally {
            _stopLock.Exit();
        }
    }

    private void SendPacketInternal(IpPacket ipPacket)
    {
        // try to send the packet with exponential backoff
        var sent = false;
        var delay = 5;
        while (true) {
            // break if the packet is sent
            if (WritePacket(ipPacket)) {
                sent = true;
                break;
            }

            // a stop: the packet goes with the run, and no wait here holds the stop. A start, a restart's too,
            // opens its device last: a packet before finds none, and would wait out the backoff for nothing
            if (!IsReady || _isStarting)
                return;

            // break if delay exceeds the max delay
            if (delay > _maxPacketSendDelayMs)
                break;

            // wait for the next try
            delay *= 2;
            Task.Delay(delay).Wait();
            WaitForTunWrite();
        }

        // log if failed to send
        if (!sent)
            VhLogger.Instance.LogWarning("Failed to send packet via TUN adapter. AdapterName: {AdapterName}",
                AdapterName);
    }

    // The reader's thread is known, so a stop made on it does not wait for itself. Never faults: a stop
    // waits for the task.
    private void RunReader()
    {
        _readerThreadId = Environment.CurrentManagedThreadId;
        try {
            StartReadingPackets();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The TUN adapter's reader ended by an error.");
        }
    }

    /// <summary>
    /// Waits for this run's reader to end, for an adapter about to free what the reader uses. A stop made on the
    /// reader's own thread, by its read errors or by a subscriber, does not wait for itself.
    /// </summary>
    /// <param name="timeout">The longest wait.</param>
    /// <returns><see langword="true"/> if the reader has ended, or the call is on its thread; otherwise, <see langword="false"/>.</returns>
    protected bool WaitForReader(TimeSpan timeout)
    {
        return Environment.CurrentManagedThreadId == _readerThreadId || _readerTask.Wait(timeout);
    }

    /// <summary>
    /// Waits for the batch being sent to end, for an adapter that has cleared the handle its WritePacket
    /// uses: that batch may still hold the handle, a later one finds it cleared. The sender writes its
    /// flag with no fence, so a process-wide barrier before the wait makes a flag set before the clearing
    /// seen here, and one after it makes all the batch did before clearing its flag seen too (ARM64 may
    /// show the clearing first). A stop made by this adapter's sender, on its own errors, does not wait for
    /// itself.
    /// </summary>
    /// <param name="timeout">The longest wait.</param>
    /// <returns><see langword="true"/> if no batch is being sent, or the call is the sender's own; otherwise, <see langword="false"/>.</returns>
    protected bool WaitForSender(TimeSpan timeout)
    {
        if (_senderErrorStopAdapter == this)
            return true;

        Interlocked.MemoryBarrierProcessWide();
        var stopwatch = Stopwatch.StartNew();
        var spinWait = new SpinWait();
        while (IsSendingBatch) {
            if (stopwatch.Elapsed > timeout)
                return false;

            // never a sleep, a whole timer tick: under traffic, a check may meet one batch after another
            spinWait.SpinOnce(sleep1Threshold: -1);
        }

        Interlocked.MemoryBarrierProcessWide();
        return true;
    }

    /// <summary>
    /// Reads packets from the interface and hands each to the transport's receivers, until a stop retires this
    /// run. It runs on the reader's task; ten read errors in a row stop the adapter.
    /// </summary>
    protected virtual void StartReadingPackets()
    {
        // Read packets from TUN adapter while this reader's run is the current one: a stop retires it, so
        // a reader held in a subscriber past its stop's wait leaves instead of joining the next run
        var runId = Task.CurrentId ?? -1;
        while (_readerRunId == runId) {
            try {
                // read next packet; no packet: wait before retrying
                var packet = ReadPacket(_mtu);
                if (packet == null)
                    WaitForTunRead();

                // a read with no exception, the wait's included, ends the reader's run of errors
                _readErrorCount = 0;
                if (packet == null)
                    continue;

                // process the packet
                OnPacketReceived(packet);
            }
            catch (Exception) when (_readerRunId != runId) {
                break; // normal stop
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in reading packets from TUN adapter.");
                _readErrorCount++;
                if (_readErrorCount < MaxIoErrorCount)
                    continue;

                // Too many errors in a row: the adapter is broken, so stop it, and recover or tell the
                // owner. Only here, not after the loop: a reader that ends with its run's stop may end
                // late, after a restart has started another run, which it must leave be. The loop's
                // check ends it: this stop, or another one under way, has retired its run.
                if (StopOnIoErrors(runId))
                    OnUnrequestedStop(ex);
            }
        }

        VhLogger.Instance.LogDebug("Finish reading the packets from the TUN adapter.");
    }

    /// <summary>
    /// Reads one packet into memory rented from <see cref="MemoryPool{T}.Shared"/>, with
    /// <see cref="ReadPacket(byte[])"/>.
    /// </summary>
    /// <param name="mtu">The size of the memory to rent: the run's MTU.</param>
    /// <returns>The packet, which owns the memory, or <see langword="null"/> if none is ready.</returns>
    /// <exception cref="ArgumentException">The bytes read are no valid IP packet.</exception>
    /// <exception cref="InvalidOperationException">The rented memory has no array behind it.</exception>
    protected virtual IpPacket? ReadPacket(int mtu)
    {
        // Allocate a memory block for the packet
        var memoryOwner = MemoryPool<byte>.Shared.Rent(mtu);

        // Get the underlying array from the memory owner
        if (!MemoryMarshal.TryGetArray<byte>(memoryOwner.Memory, out var segment))
            throw new InvalidOperationException("Could not get array from memory owner.");

        try {
            if (segment.Array == null)
                throw new InvalidOperationException("Memory owner's segment returned a null array.");

            // read packet
            var success = ReadPacket(segment.Array);
            if (success)
                return PacketBuilder.Attach(memoryOwner);

            // no more packet
            memoryOwner.Dispose();
            return null;
        }
        catch {
            memoryOwner.Dispose();
            throw;
        }
    }

    private void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
    {
        _ = CheckAdapterInterface();
        CheckPrimaryAdapterAddresses();
    }

    // a link set down on Linux keeps its address, so only this event tells of it
    private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        _ = CheckAdapterInterface();
    }

    // The OS says its network moved: is our own interface still enabled? A disabled adapter gives no I/O
    // error, its reads just stop, and its routes and DNS servers go with it, so the host's traffic leaves
    // outside the tunnel while the session shows connected: these events are the one signal of it. Under
    // the restart lock, so no restart runs between the look and the stop. An event during a start is
    // dropped here, and the start looks once at its end. Never throws, and takes no token: it only tries
    // the lock, never waits for it.
    private async Task CheckAdapterInterface()
    {
        if (!CheckAdapterInterfaceAfterNetworkChanged || !IsStarted || !_keepStarted || _isStarting ||
            IsDisposing || IsDisposed)
            return;

        try {
            using var lockScope = await _restartLock.LockAsync(TimeSpan.Zero, CancellationToken.None).Vhc();
            if (!lockScope.Succeeded || !IsStarted || !_keepStarted || _isStarting || IsDisposing || IsDisposed)
                return;

            if (IsAdapterInterfaceEnabled())
                return;

            VhLogger.Instance.LogWarning("The VPN adapter's interface is gone or disabled.");
            if (Stop(false))
                OnUnrequestedStop(new InvalidOperationException("The VPN adapter's interface is gone or disabled."));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not check the VPN adapter's interface.");
        }
    }

    // Our interface, by our name or our address, still on the IP stack, which a disabled adapter leaves
    // (Windows). Never its operational status: a fresh interface may report Down for a while after its
    // start, and a look then would stop a good start
    /// <summary>
    /// Determines whether the adapter's interface is still on the host's IP stack, found by its name or its
    /// address. Its operational status is not consulted: a fresh interface may report down for a while.
    /// </summary>
    /// <returns><see langword="true"/> if the interface is there; otherwise, <see langword="false"/>.</returns>
    protected virtual bool IsAdapterInterfaceEnabled()
    {
        var adapterAddresses = new[] { AdapterIpNetworkV4?.Prefix, AdapterIpNetworkV6?.Prefix }.OfType<IPAddress>().ToArray();
        return NetworkInterface.GetAllNetworkInterfaces()
            .Any(ni => ni.Name.Equals(AdapterName, StringComparison.OrdinalIgnoreCase) ||
                       ni.GetIPProperties().UnicastAddresses.Any(a => adapterAddresses.Contains(a.Address)));
    }

    // Restarts, or rediscovers the primary IPs, where the host's addresses moved since the last look.
    // Never throws: the OS raises it on a thread of its own, and a start that succeeded must not fail by it.
    private void CheckPrimaryAdapterAddresses()
    {
        if (_isStarting || _isStopping || _restartLock.IsLocked || IsDisposing || IsDisposed)
            return;

        try {
            // check if primary adapter addresses actually changed
            var currentAddresses = GetPrimaryAdapterAddresses();
            if (currentAddresses.SetEquals(_lastPrimaryAdapterAddresses))
                return;
            var isSameNetworks = IsSameNetworks(currentAddresses, _lastPrimaryAdapterAddresses);

            // a change a restart handles is recorded by its start: recorded here, a restart that a start under way
            // drops would leave that start's look at its end no change to see
            if (currentAddresses.Count == 0 || isSameNetworks || !RestartAfterNetworkAddressChanged)
                _lastPrimaryAdapterAddresses = currentAddresses;

            // no primary network adapter up: nothing to look for. The empty set is recorded, so any address
            // that comes back, the old one included, is a network change
            if (currentAddresses.Count == 0)
                return;

            // new addresses on the same networks: the OS rotated a temporary IPv6 address. The adapter needs its
            // primary IPs again, not a restart, whose teardown would let the host's traffic pass outside the
            // tunnel meanwhile. A socket protected by its bind cannot look past its own address, so the look is
            // unprotected there: the dead networks it aims at stay out of the tunnel's routes
            if (isSameNetworks) {
                VhLogger.Instance.LogInformation("Network addresses changed within the same networks.");
                DiscoverPrimaryAdapterIps(protect: !IsSocketProtectedByBind);
                return;
            }

            // an adapter that needs no restart takes the new network's primary IPs, looking as above, then
            // tells its owner, whose connections from the old networks are dead
            VhLogger.Instance.LogInformation("Network address changed.");
            if (!RestartAfterNetworkAddressChanged) {
                DiscoverPrimaryAdapterIps(protect: !IsSocketProtectedByBind);
                OnPrimaryNetworkChanged();
                return;
            }

            // a restart is for a started adapter: a stopped one waits for its retry, or for its owner.
            // On the pool: the lock is free, so the restart's stop would run here, inside a start's end
            if (IsStarted && _keepStarted) {
                var keepStartedToken = _keepStartedCts.Token; // this run's, not a later one's
                _ = Task.Run(() => RunRestart(keepStartedToken), keepStartedToken);
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not check the primary adapter addresses.");
        }
    }

    /// <summary>
    /// Raises <see cref="PrimaryNetworkChanged"/>.
    /// </summary>
    // protected, so a test adapter raises it: a test never moves the host's network
    protected void OnPrimaryNetworkChanged()
    {
        PrimaryNetworkChanged?.Invoke(this, EventArgs.Empty);
    }

    // The same networks: each IPv4 address, and each IPv6 one by its /64, as the OS rotates its temporary IPv6
    // addresses within their prefix (RFC 8981), daily on Windows
    internal static bool IsSameNetworks(IEnumerable<IPAddress> addresses, IEnumerable<IPAddress> otherAddresses)
    {
        return GetNetworks(addresses).SetEquals(GetNetworks(otherAddresses));
    }

    private static HashSet<IpNetwork> GetNetworks(IEnumerable<IPAddress> addresses)
    {
        return addresses
            .Select(address => new IpNetwork(address, address.IsV6() ? 64 : 32))
            .ToHashSet();
    }

    private static bool IsPrimaryAdapter(NetworkInterface networkInterface)
    {
        return networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
               networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
               !networkInterface.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase);
    }

    // The host's addresses, less our own adapter's: Windows and Linux name it after us, but Android's
    // tun0 and iOS's utunN are known by their address alone. By the address, not the network: a client's
    // virtual network is the server's whole pool, which a LAN may share.
    private HashSet<IPAddress> GetPrimaryAdapterAddresses()
    {
        var adapterAddresses = new[] { AdapterIpNetworkV4?.Prefix, AdapterIpNetworkV6?.Prefix }.OfType<IPAddress>().ToArray();
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsPrimaryAdapter)
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
            .Where(ni => !ni.Name.Equals(AdapterName, StringComparison.OrdinalIgnoreCase))
            .Select(ni => ni.GetIPProperties().UnicastAddresses.Select(a => a.Address).ToArray())
            .Where(addresses => !addresses.Any(adapterAddresses.Contains))
            .SelectMany(addresses => addresses)
            .ToHashSet();
    }

    private readonly AsyncLock _restartLock = new();

    /// <summary>
    /// Stops the adapter and starts it again with its last start's options, after two seconds.
    /// </summary>
    /// <remarks>
    /// A restart already under way, an owner's start under way, the owner's stop or a disposal makes it return
    /// without restarting; the stop or disposal is checked again after the two seconds. A failed start leaves the adapter stopped, and the adapter answers that itself: it retries with
    /// <see cref="VpnAdapterSettings.AutoRestart"/>, else raises <see cref="Failed"/>.
    /// </remarks>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">The adapter has never been started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the owner stopped the adapter meanwhile.</exception>
    public async Task Restart(CancellationToken cancellationToken)
    {
        var startOptions = _startOptions ?? throw new InvalidOperationException("The adapter has not been started.");

        // the owner's stop ends this restart too, whatever token the caller passed; only that stop
        // makes a stopped adapter nobody's concern
        var keepStartedToken = _keepStartedCts.Token;
        if (!_keepStarted || keepStartedToken.IsCancellationRequested)
            return; // the owner stopped it: nothing to restart

        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, keepStartedToken);
        await RestartUnderLock(startOptions, keepStartedToken, cancellationTokenSource.Token).Vhc();

        // an address change or a disable during the restart was left to it: see them now, outside the lock
        CheckPrimaryAdapterAddresses();
        await CheckAdapterInterface().Vhc();
    }

    private async Task RestartUnderLock(VpnAdapterOptions startOptions, CancellationToken keepStartedToken,
        CancellationToken cancellationToken)
    {
        using var lockScope = await _restartLock.LockAsync(TimeSpan.Zero, cancellationToken).Vhc();
        if (!lockScope.Succeeded)
            return; // already in progress

        // A stop or a disposal clears the keep flag, waits for this lock and then stops the adapter, so a
        // restart that sees the stop begun starts nothing; one already starting runs to its end, for that stop.
        // An owner's start under way is left alone: it looks at the addresses itself at its end.
        if (!_keepStarted || _isStarting || IsDisposing || IsDisposed)
            return;

        VhLogger.Instance.LogInformation("Restarting VPN Adapter");
        try {
            // stop the adapter first, make sure ip discovery use correct routes
            Stop(throwException: true);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).Vhc();
            if (!_keepStarted || IsDisposing || IsDisposed)
                return;

            // start the adapter with the same options; the start reads the primary IPs itself, with the adapter off
            await StartCore(startOptions, cancellationToken).Vhc();
        }
        // this restart's own failure, not the owner's stop ending it: the adapter answers it
        catch (Exception ex) when (!keepStartedToken.IsCancellationRequested) {
            OnUnrequestedStop(ex);
            throw;
        }
    }

    // The adapter stopped on its own: by I/O errors, or by a restart whose start failed. With AutoRestart
    // it starts itself again after the delay, as long as its owner keeps it started; else the owner hears
    // Failed and must act, as nothing else starts it again.
    private void OnUnrequestedStop(Exception ex)
    {
        // still up: a caller's own cancellation ended its restart before the stop
        if (IsStarted || !_keepStarted || IsDisposing || IsDisposed)
            return;

        if (_autoRestart) {
            VhLogger.Instance.LogWarning(ex, "The VPN adapter is down. Restarting it in {Delay} seconds...",
                AutoRestartDelay.TotalSeconds);
            _ = RetryRestart(_keepStartedCts.Token);
            return;
        }

        // on a task of its own: a subscriber ends its session and disposes this adapter, which must not
        // run on the adapter's I/O thread, nor under the restart lock
        VhLogger.Instance.LogError(ex, "The VPN adapter is down and stays down.");
        var failed = Failed; // this run's subscribers, not a later one's
        Task.Run(() => VhInvoker.TryInvoke("raise the adapter's Failed event", LogLevel.Error, () => failed?.Invoke(this, ex)));
    }

    // A restart on a task of its own. Its failure is logged and answered by Restart.
    private async Task RunRestart(CancellationToken cancellationToken)
    {
        try {
            await Restart(cancellationToken).Vhc();
        }
        catch (Exception) {
            // logged and answered by Restart
        }
    }

    // The retry of a failed restart, unless something started the adapter meanwhile
    private async Task RetryRestart(CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(AutoRestartDelay, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            return; // the owner's stop
        }

        if (!IsStarted)
            await RunRestart(cancellationToken).Vhc();
    }

    // A restart runs on a task of its own and would go on making what a stop removes, so a stop
    // waits for it. The lock is taken on the thread pool: no caller's synchronization context needed.
    private AsyncLock.ILockAsyncResult WaitForRestart()
    {
        return Task.Run(() => _restartLock.LockAsync(CancellationToken.None)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Stops the adapter, after any restart under way ends, before the transport is disposed.
    /// </summary>
    protected sealed override void PreDispose()
    {
        _keepStarted = false;
        _keepStartedCts.TryCancel();
        using var restartLock = WaitForRestart();
        Stop(false);
        base.PreDispose();
    }

    /// <summary>
    /// Stops watching the host's network changes, raises <see cref="Disposed"/>, then disposes the transport.
    /// </summary>
    protected override void DisposeManaged()
    {
        // notify the subscribers that the adapter is disposed
        NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;

        // a failed handler must not stop the release
        VhInvoker.TryInvoke("raise the adapter's Disposed event", LogLevel.Error,
            () => Disposed?.Invoke(this, EventArgs.Empty));
        Disposed = null;

        base.DisposeManaged();
    }
}
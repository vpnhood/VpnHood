using System.Runtime.ExceptionServices;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Filtering.DomainFiltering.Observation;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Client;

/// <summary>
/// Represents the VPN client: it finds a server, builds a session with it, and runs the session's tunnel on a VPN
/// adapter, until the session ends or the client is disposed.
/// </summary>
/// <remarks>A client connects once: its session's end disposes it, and the next connect takes a new client.</remarks>
public class VpnHoodClient : IDisposable, IAsyncDisposable
{
    private volatile bool _disposing; // a close has begun, either one: a start that it ends failed at nothing
    private bool _disposed; // the release has run
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IVpnAdapter _vpnAdapter;
    private readonly IClientSocketFactory _socketFactory;
    private ClientState _lastState = ClientState.None;
    private readonly Lock _stateEventLock = new();
    private readonly ServerFinder _serverFinder;
    private readonly ConnectedServerFile _connectedServerFile;
    private readonly AsyncLock _disposeLock = new();
    private readonly DomainFilteringService _domainFilteringService;
    // the filter stage that speaks for the server; its rules (routing declaration + SplitUnsupportedIpMode) are
    // assigned when the session is built, then only read — the device's word lives in the capture set.
    private readonly ServerIpFilter _serverIpFilter;
    private ClientSession? _session;

    internal DomainObserver DomainObserver => _domainFilteringService.DomainObserver;

    /// <summary>
    /// Occurs when the client's state changes, raised on the thread pool, each change on its own, so two raises may
    /// overlap or come out of order; the handlers are let go once the <see cref="ClientState.Disposed"/> raise has
    /// run.
    /// </summary>
    public event EventHandler? StateChanged;

    internal Token Token { get; }

    /// <summary>Gets the client's settings, from the options it was made with.</summary>
    public VpnHoodClientConfig Config { get; }

    /// <summary>
    /// Gets the proxies the client reaches the servers through, or <see langword="null"/> where it connects directly.
    /// </summary>
    public IProxyConnector? ProxyConnector { get; }

    /// <summary>Gets the client's session, or <see langword="null"/> before it is built.</summary>
    public IClientSession? Session => _session;

    /// <summary>Gets the client's session.</summary>
    /// <exception cref="InvalidOperationException">The session is not built yet.</exception>
    public IClientSession RequiredSession => _session ?? throw new InvalidOperationException("Session is not created yet.");

    /// <summary>Gets the tracker the client reports its events to, or <see langword="null"/>.</summary>
    public ITracker? Tracker { get; }
    internal ServerIpFilter ServerIpFilter => _serverIpFilter;

    /// <summary>Gets when the client's state last changed, in UTC.</summary>
    public DateTime StateChangedTime { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the requested TCP forwarding (<see cref="ClientOptions.UseTcpForwarding"/>), which the session
    /// applies at once; its status gives what is in effect, which the server and the domain filters decide too.
    /// </summary>
    /// <remarks>
    /// It starts <see langword="false"/> whatever the options say, and gives the last value set; a value set before the
    /// session is built does not change how the session starts, which is from the options.
    /// </remarks>
    public bool UseTcpForwarding { get; set { field = value; _session?.UseTcpForwarding = value; } }

    /// <summary>
    /// Gets or sets the requested dropping of the UDP bound for the tunnel (<see cref="ClientOptions.DropUdp"/>), which
    /// the session applies at once; it is in effect only while the client forwards IPv4 TCP.
    /// </summary>
    /// <remarks>
    /// It starts <see langword="false"/> whatever the options say, and gives the last value set; a value set before the
    /// session is built does not change how the session starts, which is from the options.
    /// </remarks>
    public bool DropUdp { get; set { field = value; _session?.DropUdp = value; } }

    /// <summary>
    /// Gets or sets the requested dropping of the QUIC bound for the tunnel (<see cref="ClientOptions.DropQuic"/>),
    /// which the session applies at once; its status gives what is in effect.
    /// </summary>
    /// <remarks>
    /// It starts <see langword="false"/> whatever the options say, and gives the last value set; a value set before the
    /// session is built does not change how the session starts, which is from the options.
    /// </remarks>
    public bool DropQuic { get; set { field = value; _session?.DropQuic = value; } }

    /// <summary>
    /// Gets or sets the requested protocol of the tunnel's packet channels (<see cref="ClientOptions.ChannelProtocol"/>),
    /// which the session applies at once; its status gives the one in effect, as a requested QUIC or UDP that cannot be
    /// had falls back to TCP.
    /// </summary>
    public ChannelProtocol ChannelProtocol { get; set { field = value; _session?.ChannelProtocol = value; } }

    /// <summary>
    /// Gets the error that ended the client's connect, or else its session's last one, or <see langword="null"/>; a
    /// connect's error is not kept where a shutdown was asked for meanwhile.
    /// </summary>
    public Exception? LastException { get => field ?? _session?.LastException; private set; }

    /// <summary>
    /// Gets or sets what happens to a destination the server does not route (<see cref="ClientOptions.UnroutedIpMode"/>);
    /// it applies at once.
    /// </summary>
    /// <remarks>
    /// Only the filter's verdict changes: the VPN adapter keeps what it captures from the connect, so a change that
    /// needs it to capture otherwise, as from Exclude to Block against a server that splits, takes a reconnect.
    /// </remarks>
    public SplitUnsupportedIpMode UnroutedIpMode {
        get => _serverIpFilter.UnroutedIpMode;
        set => _serverIpFilter.UnroutedIpMode = value;
    }

    /// <summary>
    /// Gets or sets what happens to IPv6 where the server carries none (<see cref="ClientOptions.UnsupportedIpV6Mode"/>);
    /// it applies at once.
    /// </summary>
    /// <remarks>As with <see cref="UnroutedIpMode"/>, only the filter's verdict changes.</remarks>
    public SplitUnsupportedIpMode UnsupportedIpV6Mode {
        get => _serverIpFilter.UnsupportedIpV6Mode;
        set => _serverIpFilter.UnsupportedIpV6Mode = value;
    }

    /// <summary>
    /// Gets the client's filters, the outermost stages over the caller's, so its owner can roll a reconfigure down them
    /// (<c>IIpFilter.Reconfigure</c>).
    /// </summary>
    /// <remarks>
    /// The client neither knows nor cares what reacts: the stages that own an outside configuration read it again, the
    /// change rolls back up, and the caches and the reading of server names follow by themselves.
    /// </remarks>
    public NetFilter NetFilter { get; }

    /// <summary>Initializes a new instance of the <see cref="VpnHoodClient"/> class.</summary>
    /// <param name="vpnAdapter">
    /// The VPN adapter, which the connect starts; the client disposes it with itself where
    /// <see cref="ClientOptions.AutoDisposeVpnAdapter"/>, and the adapter's end disposes the client.
    /// </param>
    /// <param name="socketFactory">
    /// The factory of the client's sockets; the client protects each socket it makes from the VPN adapter, where the
    /// adapter can, but not the ones the factory's QUIC opens for itself.
    /// </param>
    /// <param name="netFilter">
    /// The caller's filters, the splits by IP and by domain and the IP mapper, or <see langword="null"/>.
    /// </param>
    /// <param name="proxyConnector">
    /// The proxies to reach the servers through, which the client owns, or <see langword="null"/> to connect directly.
    /// </param>
    /// <param name="tracker">The tracker to report the client's events to, or <see langword="null"/>.</param>
    /// <param name="workingFolder">The client's folder, which it makes where it is missing.</param>
    /// <param name="options">The options.</param>
    /// <param name="knownHostAddresses">
    /// The addresses the access key's host name gave already, which the connect takes rather than asking again, or
    /// <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The options' longest packet channel age is below their shortest and not infinite.
    /// </exception>
    /// <exception cref="FormatException">The access key is not valid.</exception>
    /// <exception cref="NotSupportedException">The access key's version is not 4.</exception>
    /// <exception cref="System.Text.Json.JsonException">The access key's JSON is not valid.</exception>
    /// <exception cref="InvalidDataException">The access key's JSON is <c>null</c>.</exception>
    /// <exception cref="OverflowException">A duration in the access key is out of range.</exception>
    /// <exception cref="InvalidOperationException">The access key has no farm secret.</exception>
    public VpnHoodClient(
        IVpnAdapter vpnAdapter,
        IClientSocketFactory socketFactory,
        NetFilter? netFilter,
        IProxyConnector? proxyConnector,
        ITracker? tracker,
        string workingFolder,
        ClientOptions options,
        HostNameAddresses? knownHostAddresses = null)
    {
        if (!VhUtils.IsInfinite(options.MaxPacketChannelTimespan) &&
            options.MaxPacketChannelTimespan < options.MinPacketChannelTimespan)
            throw new ArgumentNullException(nameof(options.MaxPacketChannelTimespan),
                $"{nameof(options.MaxPacketChannelTimespan)} must be bigger or equal than {nameof(options.MinPacketChannelTimespan)}.");

        // build config
        Config = new VpnHoodClientConfig {
            AllowAnonymousTracker = options.AllowAnonymousTracker,
            MinPacketChannelLifespan = options.MinPacketChannelTimespan,
            MaxPacketChannelLifespan = options.MaxPacketChannelTimespan,
            AutoDisposeVpnAdapter = options.AutoDisposeVpnAdapter,
            Transport = options.Transport,
            PlanId = options.PlanId,
            AccessCode = options.AccessCode,
            ExcludeApps = options.ExcludeApps,
            IncludeApps = options.IncludeApps,
            IncludeIpRangesByDevice = options.IncludeIpRangesByDevice,
            DnsServers = options.DnsServers,
            SessionName = options.SessionName,
            AllowStreamReuse = options.AllowChannelReuse,
            Version = options.Version,
            UserAgent = options.UserAgent,
            ClientId = options.ClientId,
            IncludeLocalNetwork = !options.SplitLocalNetwork, // split is out of the VPN, the reverse of include
            SplitDnsMode = options.SplitDnsMode,
            UnroutedIpMode = options.UnroutedIpMode,
            UnsupportedIpV6Mode = options.UnsupportedIpV6Mode,
            UseTcpForwarding = options.UseTcpForwarding,
            DropUdp = options.DropUdp,
            DropQuic = options.DropQuic,
            UserReview = options.UserReview,
            UseWebSocket = options.DebugData1?.Contains("/disable-WebSocket", StringComparison.OrdinalIgnoreCase) is null or false
        };

        Token = Token.FromAccessKey(options.AccessKey);
        Directory.CreateDirectory(workingFolder);

        // the client owns this decoration so that no caller can supply an unprotected factory; services
        // built outside (e.g. the proxy connector) are handed the decorated one in Connect
        // make sure the decorated factory is used in the rest of the code
        socketFactory = ClientHelper.CreateSocketFactory(socketFactory, vpnAdapter, options);
        _socketFactory = socketFactory;
        _vpnAdapter = vpnAdapter;
        Tracker = tracker;
        ChannelProtocol = options.ChannelProtocol;
        ProxyConnector = proxyConnector;
        _connectedServerFile = new ConnectedServerFile(Path.Combine(workingFolder, "connected-servers"), Token.TokenId);

        // Prepare filters. The gate chain (e.g. the split-ip/split-domain dbs) is supplied by the caller
        // via netFilter; the client only adds its own gates and the caching layer on top of it.
        // Every stage is a veto gate: it may Exclude (bypass) or Block (drop); Default means "no objection"
        // and undecided traffic tunnels (fail-closed: a missing gate keeps traffic inside the VPN, it never
        // leaks around it). The StaticIpFilter vetoes non-members of the server∩device allow set.
        // The handed-in chains may change at runtime (the host live-swaps the split gates on reconfigure);
        // the change event rolls up the pipe and the cached stages invalidate themselves — the client
        // never needs to know a swap happened.
        _serverIpFilter = new ServerIpFilter(netFilter?.IpFilter);
        // Domain gates preempt the IP gates, and their include set is the override lane — a member domain is
        // forced through the tunnel past any IP-gate veto (domains are more specific knowledge than IPs).
        NetFilter = new NetFilter {
            IpFilter = new CachedIpFilter(_serverIpFilter, TimeSpan.FromMinutes(60)),
            DomainFilter = new CachedDomainFilter(netFilter?.DomainFilter, TimeSpan.FromMinutes(60)),
            IpMapper = netFilter?.IpMapper
        };

        // server finder
        _serverFinder = new ServerFinder(socketFactory,
            farmSecret: Token.ServerToken.Secret ?? throw new InvalidOperationException("The access key has no farm secret."),
            serverLocation: options.ServerLocation,
            serverQueryTimeout: options.Transport.ServerQueryTimeout,
            endPointStrategy: options.EndPointStrategy,
            customServerEndpoints: options.CustomServerEndpoints ?? [],
            tracker: options.AllowEndPointTracker ? tracker : null,
            proxyConnector: ProxyConnector,
            includeIpV6: _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6),
            useKillSwitch: options.UseKillSwitch,
            knownHostAddresses: knownHostAddresses);

        // SNI extraction is sensitive and costly, so it runs only while a domain rule could consume it
        // (or SNI logging is forced). Nobody tells the client: it derives the answer from its own pipe
        // (IsEmpty) and re-derives whenever the pipe announces a change — rules appearing or vanishing
        // in a live gate swap toggle extraction with no outside caller.
        _domainFilteringService = new DomainFilteringService(
            NetFilter.DomainFilter,
            sniEventId: GeneralEventId.Sni,
            tlsBufferSize: TransportDefaults.PrefetchStreamBufferSize);
        var forceLogSni = options.ForceLogSni;
        _domainFilteringService.IsEnabled = forceLogSni || !NetFilter.DomainFilter.IsEmpty;
        NetFilter.DomainFilter.Changed += (_, _) =>
            _domainFilteringService.IsEnabled = forceLogSni || !NetFilter.DomainFilter.IsEmpty;

        // init vpnAdapter events
        vpnAdapter.Disposed += (_, _) => _ = DisposeAsync();
    }

    /// <summary>
    /// Gets the progress of the state's work, while the client finds a server or checks its proxies, or
    /// <see langword="null"/>.
    /// </summary>
    public ProgressStatus? StateProgress =>
        State switch {
            ClientState.FindingReachableServer or ClientState.FindingBestServer => _serverFinder.Progress,
            ClientState.ValidatingProxies => ProxyConnector?.Progress,
            _ => null
        };

    /// <summary>Gets the client's state.</summary>
    public ClientState State {
        get;
        private set {
            field = value;
            FireStateChanged();
        }
    } = ClientState.None;

    private void FireStateChanged()
    {
        var state = State;
        lock (_stateEventLock) {
            if (_lastState == state)
                return;
            _lastState = state;
            StateChangedTime = FastDateTime.UtcNow;
        }

        VhLogger.Instance.LogInformation("Client state is changed. NewState: {NewState}", state);
        Task.Run(() => {
            StateChanged?.Invoke(this, EventArgs.Empty);

            // no more event after the Disposed one, which only its own raise may end: an earlier raise that ends
            // after the client is disposed would leave it no handler
            if (state == ClientState.Disposed)
                StateChanged = null;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Connects: finds a server, builds a session with it, and starts the session's tunnel on the VPN adapter.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the client is connected.</returns>
    /// <remarks>
    /// A failure disposes the client; where the session ended with an error of its own meanwhile, that error is thrown,
    /// rather than how the connect found out.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="Exception">
    /// The client has connected, or is connecting, already; a redirect named no server endpoint; or the ad the session
    /// required failed, with that failure.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The VPN adapter has started already; the tunnel's MTU is below 1280; or no DNS server can go through the tunnel
    /// where <see cref="SplitDnsMode.IncludeAll"/> keeps DNS in it.
    /// </exception>
    /// <exception cref="VpnHood.Core.Client.Abstractions.Exceptions.UnreachableServerException">No server answered.</exception>
    /// <exception cref="VpnHood.Core.Client.Abstractions.Exceptions.UnreachableProxyServerException">No proxy server answered.</exception>
    /// <exception cref="NotSupportedException">
    /// The server's protocol is outside the client's, it named no client address, or it carries TCP neither as packets
    /// nor by forwarding.
    /// </exception>
    /// <exception cref="VpnHood.Core.Client.Abstractions.Exceptions.ConnectionTimeoutException">The client could not connect to the server in time.</exception>
    /// <exception cref="TimeoutException">A request to the server got no answer in time.</exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.SessionException">The server refused the session.</exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.MaintenanceException">The server is in maintenance.</exception>
    /// <exception cref="UnauthorizedAccessException">The server refused the request's proof.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the client was disposed meanwhile.
    /// </exception>
    public async Task Connect(CancellationToken cancellationToken)
    {
        try {
            await Connect2(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            // What closed the session, when an error did, says more than how the start found out - often a
            // disposed session. A shutdown asked for meanwhile, a disconnect say, leaves no error to show.
            var sessionException = _session?.LastException;
            if (sessionException == null && !_disposing)
                LastException = ex;

            await DisposeAsync().Vhc();
            if (sessionException != null && sessionException != ex)
                ExceptionDispatchInfo.Throw(sessionException);

            throw;
        }
    }

    private async Task Connect2(CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        // create connection log scope
        using var scope = VhLogger.Instance.BeginScope("Client");
        if (State != ClientState.None)
            throw new Exception("Connection is already in progress.");

        // Preparing device;
        if (_vpnAdapter.IsStarted) //make sure it is not a shared packet capture
            throw new InvalidOperationException("VpnAdapter should not be started before connect.");

        // Connecting. Must before IsIpv6Supported
        State = ClientState.Connecting;

        var sessionBuilder = new ClientSessionBuilder(
            vpnAdapter: _vpnAdapter,
            socketFactory: _socketFactory,
            token: Token,
            config: Config,
            tracker: Tracker,
            serverFinder: _serverFinder,
            connectedServerFile: _connectedServerFile,
            proxyConnector: ProxyConnector,
            domainFilteringService: _domainFilteringService,
            netFilter: NetFilter,
            serverIpFilter: _serverIpFilter,
            channelProtocol: ChannelProtocol,
            setState: state => State = state);

        _session = await sessionBuilder.Build(_cancellationTokenSource.Token, cancellationToken).Vhc();
        _session.StateChanged += Session_StateChanged;

        // before the adapter starts: a crash from then on leaves it for the redial
        await _connectedServerFile.Write(_session.VpnEndPoint, cancellationToken).Vhc();
        await _session.Start(cancellationToken).Vhc();

        State = ClientState.Connected;
    }

    private void Session_StateChanged(object? sender, EventArgs e)
    {
        // the client's state is final once its release has run: a session whose build ended after the close does not
        // move it
        if (_disposed)
            return;

        if (_session is null)
            throw new InvalidOperationException("How a null session session can fire an event!");

        // disposed client if the session is disposed
        if (_session is { State: ClientState.Disposed })
            Dispose();

        State = _session.State;
    }

    internal Task UpdateSessionStatus(CancellationToken cancellationToken)
    {
        return _session?.UpdateStatus(cancellationToken) ?? Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The graceful close: it closes the session, which tells its server where it can, and the proxies, and releases
    /// the client as <see cref="Dispose"/> does, which the session's end may have started meanwhile. A second call waits
    /// for the first one's end.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end - a disconnect during connect, say, where the failed connect
        // closes too; the adapter's Disposed handler, raised in that release, only starts one. No early return after
        // it: the steps below join what a quick close left running
        using var lockResult = await _disposeLock.LockAsync(CancellationToken.None).Vhc();

        // only the first close logs its start
        if (!Interlocked.Exchange(ref _disposing, true))
            VhLogger.Instance.LogInformation("Client is shutting down asynchronously...");
        await _cancellationTokenSource.TryCancelAsync().Vhc();

        // a failed session close must not keep the adapter from its release below
        if (_session != null)
            await _session.TryDisposeAsync(LogLevel.Warning).Vhc();

        // flush and dispose the proxy connector while we can still await
        await ProxyConnector.TryDisposeAsync().Vhc();

        // the rest is local, as in the quick close, whose cancel and closes of the session and the proxy connector
        // find this one's done. The session's disposed state may have run it already
        Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The quick close: the session, the proxies, the filters and, where <see cref="ClientOptions.AutoDisposeVpnAdapter"/>,
    /// the VPN adapter close without waiting, some of their closes going on in the background, and the state becomes
    /// <see cref="ClientState.Disposed"/>.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // a graceful close that came first has logged the start
        if (!Interlocked.Exchange(ref _disposing, true))
            VhLogger.Instance.LogInformation("Client is shutting down...");

        _cancellationTokenSource.TryCancel();
        _session?.Dispose();

        // a close, not a crash: the next connect looks its server up again
        _connectedServerFile.TryDelete();

        // dispose ProxyConnector before adapter get closed and it needs Adapter's SocketFactory.
        // this sync path cannot await; the flush runs in the background and only touches the
        // endpoint db. The session's connector service closes in the background too, so a proxy
        // failure it records after the flush is not saved
        VhLogger.Instance.LogDebug("Disposing ProxyConnector...");
        _ = ProxyConnector.TryDisposeAsync();

        _cancellationTokenSource.Dispose();
        _session?.StateChanged -= Session_StateChanged;
        _domainFilteringService.Dispose();
        NetFilter.Dispose();

        // disposing adapter
        if (Config.AutoDisposeVpnAdapter) {
            VhLogger.Instance.LogDebug("Disposing Adapter...");
            _vpnAdapter.Dispose();
        }

        //everything is clean
        State = ClientState.Disposed;

        // Changing state fire events in a task, so we should not do it immediately after disposing
        // StateChanged = null;
        VhLogger.Instance.LogInformation("Bye Bye!");
    }
}

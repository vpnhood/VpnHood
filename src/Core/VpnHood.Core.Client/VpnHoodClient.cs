using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Filtering.DomainFiltering.Observation;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Monitoring;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

public class VpnHoodClient : IDisposable, IAsyncDisposable
{
    private bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IVpnAdapter _vpnAdapter;
    private readonly ISocketFactory _socketFactory;
    private ClientState _lastState = ClientState.None;
    private readonly Lock _stateEventLock = new();
    private readonly ServerFinder _serverFinder;
    private readonly AsyncLock _disposeLock = new();
    private readonly NetFilter _netFilter;
    private readonly DomainFilteringService _domainFilteringService;
    private readonly StaticIpFilter _staticIpFilter;
    private ClientSession? _session;

    public DomainObserver DomainObserver => _domainFilteringService.DomainObserver;
    public event EventHandler? StateChanged;
    public Token Token { get; }
    public VpnHoodClientConfig Config { get; }
    // null when the caller configured no proxy: connections then go direct
    public IProxyConnector? ProxyConnector { get; }
    public IClientSession? Session => _session;
    public IClientSession RequiredSession => _session ?? throw new InvalidOperationException("Session is not created yet.");
    public ITracker? Tracker { get; }
    public IpRangeOrderedList SessionIncludeIpRangesByApp => _staticIpFilter.IncludeRanges;
    public DateTime StateChangedTime { get; private set; } = DateTime.Now;
    public bool UseTcpProxy { get; set { field = value; _session?.UseTcpProxy = value; } }
    public bool DropUdp { get; set { field = value; _session?.DropUdp = value; } }
    public bool DropQuic { get; set { field = value; _session?.DropQuic = value; } }
    public ChannelProtocol ChannelProtocol { get; set { field = value; _session?.ChannelProtocol = value; } }
    public Exception? LastException { get => field ?? _session?.LastException; private set; }

    public VpnHoodClient(
        IVpnAdapter vpnAdapter,
        ISocketFactory socketFactory,
        NetFilter? netFilter,
        IProxyConnector? proxyConnector,
        ITracker? tracker,
        ClientOptions options)
    {
        if (!VhUtils.IsInfinite(options.MaxPacketChannelTimespan) &&
            options.MaxPacketChannelTimespan < options.MinPacketChannelTimespan)
            throw new ArgumentNullException(nameof(options.MaxPacketChannelTimespan),
                $"{nameof(options.MaxPacketChannelTimespan)} must be bigger or equal than {nameof(options.MinPacketChannelTimespan)}.");

        // build config
        Config = new VpnHoodClientConfig {
            TcpProxyCatcherAddressIpV6 = options.TcpProxyCatcherAddressIpV6,
            TcpProxyCatcherAddressIpV4 = options.TcpProxyCatcherAddressIpV4,
            AllowAnonymousTracker = options.AllowAnonymousTracker,
            MinPacketChannelLifespan = options.MinPacketChannelTimespan,
            MaxPacketChannelLifespan = options.MaxPacketChannelTimespan,
            AutoDisposeVpnAdapter = options.AutoDisposeVpnAdapter,
            MaxPacketChannelCount = options.MaxPacketChannelCount,
            TcpConnectTimeout = options.ConnectTimeout,
            PlanId = options.PlanId,
            AccessCode = options.AccessCode,
            ExcludeApps = options.ExcludeApps,
            IncludeApps = options.IncludeApps,
            IncludeIpRangesByDevice = options.IncludeIpRangesByDevice,
            DnsServers = options.DnsServers,
            SessionName = options.SessionName,
            AllowStreamReuse = options.AllowChannelReuse,
            UnstableTimeout = options.UnstableTimeout,
            AutoWaitTimeout = options.AutoWaitTimeout,
            Version = options.Version,
            UserAgent = options.UserAgent,
            ClientId = options.ClientId,
            SessionTimeout = options.SessionTimeout,
            IncludeLocalNetwork = options.SplitLocalNetwork,
            IsTcpProxySupported = options.IsTcpProxySupported,
            UseTcpProxy = options.UseTcpProxy,
            DropUdp = options.DropUdp,
            DropQuic = options.DropQuic,
            UserReview = options.UserReview,
            StreamProxyBufferSize = options.StreamProxyBufferSize ?? TunnelDefaults.ClientStreamProxyBufferSize,
            TcpKernelBufferSize = options.TcpKernelBufferSize,
            UdpProxyBufferSize = options.UdpProxyBufferSize ?? TunnelDefaults.ClientUdpProxyBufferSize,
            PacketChannelBufferSize = options.PacketChannelBufferSize,
            UseWebSocket = options.DebugData1?.Contains("/disable-WebSocket", StringComparison.OrdinalIgnoreCase) is null or false,
            UseOsTcpStack = options.DebugData1?.Contains("/os-tcp-stack", StringComparison.OrdinalIgnoreCase) is true
        };

        Token = Token.FromAccessKey(options.AccessKey);

        // the client owns this decoration so that no caller can supply an unprotected factory; services
        // built outside (e.g. the proxy connector) are handed the decorated one in Connect
        // make sure the decorated factory is used in the rest of the code
        socketFactory = ClientHelper.CreateSocketFactory(socketFactory, vpnAdapter, options);
        _socketFactory = socketFactory;
        _vpnAdapter = vpnAdapter;
        Tracker = tracker;
        ChannelProtocol = options.ChannelProtocol;
        ProxyConnector = proxyConnector;

        // Prepare filters. The gate chain (e.g. the split-ip/split-domain dbs) is supplied by the caller
        // via netFilter; the client only adds its own gates and the caching layer on top of it.
        // Every stage is a veto gate: it may Exclude (bypass) or Block (drop); Default means "no objection"
        // and undecided traffic tunnels (fail-closed: a missing gate keeps traffic inside the VPN, it never
        // leaks around it). The StaticIpFilter vetoes non-members of the server∩device allow set.
        _staticIpFilter = new StaticIpFilter(netFilter?.IpFilter);

        // Domain gates preempt the IP gates, and their include set is the override lane — a member domain is
        // forced through the tunnel past any IP-gate veto (domains are more specific knowledge than IPs).
        _netFilter = new NetFilter {
            IpFilter = new CachedIpFilter(_staticIpFilter, TimeSpan.FromMinutes(60)),
            DomainFilter = new CachedDomainFilter(netFilter?.DomainFilter, TimeSpan.FromMinutes(60)),
            IpMapper = netFilter?.IpMapper
        };

        // server finder
        _serverFinder = new ServerFinder(socketFactory,
            serverLocation: options.ServerLocation,
            serverQueryTimeout: options.ServerQueryTimeout,
            endPointStrategy: options.EndPointStrategy,
            customServerEndpoints: options.CustomServerEndpoints ?? [],
            tracker: options.AllowEndPointTracker ? tracker : null,
            proxyConnector: ProxyConnector,
            includeIpV6: _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6));


        // SNI is sensitive, must be explicitly enabled
        _domainFilteringService = new DomainFilteringService(
            _netFilter.DomainFilter,
            sniEventId: GeneralEventId.Sni,
            tlsBufferSize: TunnelDefaults.PrefetchStreamBufferSize);
        _domainFilteringService.IsEnabled |= options.ForceLogSni || options.UseDomainFilter;

        // init vpnAdapter events
        vpnAdapter.Disposed += (_, _) => _ = DisposeAsync();
    }

    public ProgressStatus? StateProgress =>
        State switch {
            ClientState.FindingReachableServer or ClientState.FindingBestServer => _serverFinder.Progress,
            ClientState.ValidatingProxies => ProxyConnector?.Progress,
            _ => null
        };

    public ClientState State {
        get;
        private set {
            field = value;
            FireStateChanged();
        }
    } = ClientState.None;

    private void FireStateChanged()
    {
        lock (_stateEventLock) {
            if (_lastState == State)
                return;
            _lastState = State;
            StateChangedTime = FastDateTime.Now;
        }

        VhLogger.Instance.LogInformation("Client state is changed. NewState: {NewState}", State);
        Task.Run(() => {
            StateChanged?.Invoke(this, EventArgs.Empty);
            if (State == ClientState.Disposed)
                StateChanged = null; //no more event will be raised after disposed
        }, CancellationToken.None);
    }

    public async Task Connect(CancellationToken cancellationToken = default)
    {
        try {
            await Connect2(cancellationToken);
        }
        catch (Exception ex) {
            LastException = ex;
            await DisposeAsync();
            throw;
        }

    }

    public async Task Connect2(CancellationToken cancellationToken = default)
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
            proxyConnector: ProxyConnector,
            domainFilteringService: _domainFilteringService,
            netFilter: _netFilter,
            staticIpFilter: _staticIpFilter,
            channelProtocol: ChannelProtocol,
            setState: state => State = state);

        _session = await sessionBuilder.Build(_cancellationTokenSource.Token, cancellationToken).Vhc();
        _session.StateChanged += Session_StateChanged;
        await _session.Start(cancellationToken);

        State = ClientState.Connected;
    }

    private void Session_StateChanged(object? sender, EventArgs e)
    {
        if (_session is null)
            throw new InvalidOperationException("How a null session session can fire an event!");

        // disposed client if the session is disposed
        if (_session is { State: ClientState.Disposed })
            Dispose();

        State = _session.State;
    }

    public Task UpdateSessionStatus(CancellationToken cancellationToken)
    {
        return _session?.UpdateStatus(cancellationToken) ?? Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        VhLogger.Instance.LogInformation("Client is shutting down asynchronously...");

        if (_session != null) {
            await _cancellationTokenSource.TryCancelAsync();
            await _session.DisposeAsync();
        }

        // flush and dispose the proxy connector while we can still await; the sync Dispose
        // below then skips it (connector dispose is idempotent)
        await ProxyConnector.SafeDisposeAsync();

        Dispose();
    }

    public void Dispose()
    {
        lock (_disposeLock) {
            if (_disposed) return;
            _disposed = true;
        }

        // shutdown
        VhLogger.Instance.LogInformation("Client is shutting down...");
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();

        // close session
        _session?.Dispose();
        _session?.StateChanged -= Session_StateChanged;

        _netFilter.Dispose();

        // dispose ConnectorService before ProxyConnector as it uses ProxyConnector
        VhLogger.Instance.LogDebug("Disposing ConnectorService...");

        // dispose ProxyConnector before adapter get closed and it needs Adapter's SocketFactory.
        // this sync path cannot await; the flush runs in the background and only touches the
        // endpoint db (no-op when DisposeAsync already disposed the connector)
        VhLogger.Instance.LogDebug("Disposing ProxyConnector...");
        _ = ProxyConnector.SafeDisposeAsync();

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
using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Trackers;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Filtering.DomainFiltering.Observation;
using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Monitoring;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

public class VpnHoodClient : IDisposable, IAsyncDisposable
{
    private bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IVpnAdapter _vpnAdapter;
    private readonly ISocketFactory _socketFactory;
    private byte[]? _sessionKey;
    private ClientState _lastState = ClientState.None;
    private readonly Lock _stateEventLock = new();
    private ConnectorService? _connectorService;
    private readonly ServerFinder _serverFinder;
    private ulong? _sessionId;
    private readonly AsyncLock _disposeLock = new();
    private TaskCompletionSource? _waitForAdCts;
    private readonly NetFilter _netFilter;
    private readonly DomainFilteringService _domainFilteringService;
    private readonly StaticIpFilter _staticIpFilter;
    private ClientSession? _session;

    private ConnectorService ConnectorService => VhUtils.GetRequiredInstance(_connectorService);
    public DomainObserver DomainObserver => _domainFilteringService.DomainObserver;
    public event EventHandler? StateChanged;
    public Token Token { get; }
    public VpnHoodClientConfig Config { get; }
    public ProxyEndPointManager ProxyEndPointManager { get; }
    public ISessionStatus? SessionStatus => _session?.Status;
    public ITracker? Tracker { get; }
    public IPEndPoint? HostTcpEndPoint => _connectorService?.VpnEndPoint.TcpEndPoint;
    public IPEndPoint? HostUdpEndPoint => _session?.Config.HostUdpEndPoint;
    public IpRangeOrderedList? SessionIncludeIpRangesByDevice { get; private set; }
    public IpRangeOrderedList SessionIncludeIpRangesByApp => _staticIpFilter.IncludeRanges;
    public byte[] SessionKey => _sessionKey ?? throw new InvalidOperationException($"{nameof(SessionKey)} has not been initialized.");
    public ulong SessionId => _sessionId ?? throw new InvalidOperationException("SessionId has not been initialized.");
    public SessionInfo? SessionInfo { get; private set; }
    public DateTime StateChangedTime { get; private set; } = DateTime.Now;
    public bool UseTcpProxy { get; set { field = value; _session?.UseTcpProxy = value; } }
    public bool DropUdp { get; set { field = value; _session?.DropUdp = value; } }
    public bool DropQuic { get; set { field = value; _session?.DropQuic = value; } }
    public ChannelProtocol ChannelProtocol { get; set { field = value; _session?.ChannelProtocol = value; } }
    public Exception? LastException {get=> field ?? _session?.LastException; private set; }

    public VpnHoodClient(
        IVpnAdapter vpnAdapter,
        ISocketFactory socketFactory,
        NetFilter? netFilter,
        string? storageFolder,
        ITracker? tracker,
        ClientOptions options)
    {
        if (!VhUtils.IsInfinite(options.MaxPacketChannelTimespan) &&
            options.MaxPacketChannelTimespan < options.MinPacketChannelTimespan)
            throw new ArgumentNullException(nameof(options.MaxPacketChannelTimespan),
                $"{nameof(options.MaxPacketChannelTimespan)} must be bigger or equal than {nameof(options.MinPacketChannelTimespan)}.");

        if (string.IsNullOrEmpty(storageFolder))
            storageFolder = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "vpn-service");

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
            IncludeIpRangesByApp = options.IncludeIpRangesByApp,
            IncludeIpRangesByDevice = options.IncludeIpRangesByDevice,
            DnsServers = options.DnsServers,
            SessionName = options.SessionName,
            AllowTcpReuse = options.AllowTcpReuse,
            UnstableTimeout = options.UnstableTimeout,
            AutoWaitTimeout = options.AutoWaitTimeout,
            Version = options.Version,
            UserAgent = options.UserAgent,
            ClientId = options.ClientId,
            SessionTimeout = options.SessionTimeout,
            IncludeLocalNetwork = options.IncludeLocalNetwork,
            IsTcpProxySupported = options.IsTcpProxySupported,
            UseTcpProxy = options.UseTcpProxy,
            DropUdp = options.DropUdp,
            DropQuic = options.DropQuic,
            UserReview = options.UserReview,
            StreamProxyBufferSize = options.StreamProxyBufferSize ?? TunnelDefaults.ClientStreamProxyBufferSize,
            UdpProxyBufferSize = options.UdpProxyBufferSize ?? TunnelDefaults.ClientUdpProxyBufferSize,
            UseWebSocket = options.DebugData1?
                .Contains("/disable-WebSocket", StringComparison.OrdinalIgnoreCase) is null or false
        };

        Token = Token.FromAccessKey(options.AccessKey);
        socketFactory = new AdapterSocketFactory(vpnAdapter, socketFactory);
        _socketFactory = socketFactory;
        _vpnAdapter = vpnAdapter;
        Tracker = tracker;
        ChannelProtocol = options.ChannelProtocol;

        // Prepare filters
        _staticIpFilter = new StaticIpFilter(netFilter?.IpFilter);
        var staticDomainFilter = new StaticDomainFilter(netFilter?.DomainFilter) {
            Blocks = options.DomainFilterPolicy.Blocks,
            Excludes = options.DomainFilterPolicy.Excludes,
            Includes = options.DomainFilterPolicy.Includes
        };
        _netFilter = new NetFilter {
            IpFilter = new CachedIpFilter(_staticIpFilter, TimeSpan.FromMinutes(60)),
            DomainFilter = new CachedDomainFilter(staticDomainFilter, TimeSpan.FromMinutes(60)),
            IpMapper = netFilter?.IpMapper
        };

        // External Proxies
        ProxyEndPointManager = new ProxyEndPointManager(
            proxyOptions: options.ProxyOptions ?? new ProxyOptions(),
            storagePath: Path.Combine(storageFolder, "proxies"),
            socketFactory: socketFactory,
            serverCheckTimeout: options.ServerQueryTimeout);

        // server finder
        _serverFinder = new ServerFinder(socketFactory,
            serverLocation: options.ServerLocation,
            serverQueryTimeout: options.ServerQueryTimeout,
            endPointStrategy: options.EndPointStrategy,
            customServerEndpoints: options.CustomServerEndpoints ?? [],
            tracker: options.AllowEndPointTracker ? tracker : null,
            proxyEndPointManager: ProxyEndPointManager,
            includeIpV6: _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6));


        // SNI is sensitive, must be explicitly enabled
        _domainFilteringService = new DomainFilteringService(
            _netFilter.DomainFilter,
            sniEventId: GeneralEventId.Sni,
            tlsBufferSize: TunnelDefaults.PrefetchStreamBufferSize);
        _domainFilteringService.IsEnabled |= options.ForceLogSni || !staticDomainFilter.IsEmpty;

        // init vpnAdapter events
        vpnAdapter.Disposed += (_, _) => _ = DisposeAsync();
    }

    public ProgressStatus? StateProgress =>
        State switch {
            ClientState.FindingReachableServer or ClientState.FindingBestServer => _serverFinder.Progress,
            ClientState.ValidatingProxies => ProxyEndPointManager.Progress,
            _ => null
        };

    public ClientState State {
        get {
            if (field is ClientState.Disposed or ClientState.Disconnecting)
                return field;

            // waiting for ad
            if (_waitForAdCts?.Task.IsCompleted is false)
                return ClientState.WaitingForAd;

            // waiting for ad (step 2)
            if (_session?.PassthroughForAd == true && field == ClientState.Connected)
                return ClientState.WaitingForAdEx;

            // return session state if session is created, otherwise return client state
            return field;
        }
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

        // merge cancellation tokens
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationTokenSource.Token, cancellationToken);

        // create connection log scope
        using var scope = VhLogger.Instance.BeginScope("Client");
        if (State != ClientState.None)
            throw new Exception("Connection is already in progress.");

        // Preparing device;
        if (_vpnAdapter.IsStarted) //make sure it is not a shared packet capture
            throw new InvalidOperationException("VpnAdapter should not be started before connect.");

        // Connecting. Must before IsIpv6Supported
        State = ClientState.Connecting;

        // report config
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        VhLogger.Instance.LogInformation(
            "DropUdp: {DropUdp}, VpnProtocol: {VpnProtocol}, " +
            "IncludeLocalNetwork: {IncludeLocalNetwork}, MinWorkerThreads: {WorkerThreads}, " +
            "CompletionPortThreads: {CompletionPortThreads}, ClientIpV6: {ClientIpV6}, ProcessId: {ProcessId}",
            Config.DropUdp, ChannelProtocol, Config.IncludeLocalNetwork, workerThreads, completionPortThreads,
            _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6), Process.GetCurrentProcess().Id);

        // report version
        VhLogger.Instance.LogInformation(
            "ClientVersion: {ClientVersion}, " +
            "ClientMinProtocolVersion: {ClientMinProtocolVersion}, ClientMaxProtocolVersion: {ClientMaxProtocolVersion}, " +
            "ClientId: {ClientId}",
            Config.Version, VpnHoodClientConfig.MinProtocolVersion, VpnHoodClientConfig.MaxProtocolVersion,
            VhLogger.FormatId(Config.ClientId));

        // validate proxy servers
        if (ProxyEndPointManager.IsEnabled) {
            State = ClientState.ValidatingProxies;
            await ProxyEndPointManager.CheckServers(linkedCts.Token).Vhc();

            // log proxy status
            VhLogger.Instance.LogInformation("Proxy servers: {Count}",
                ProxyEndPointManager.Status.ProxyEndPointInfos.Count(x => x.Status.ErrorMessage is null));

            // check is any proxy succeeded
            if (!ProxyEndPointManager.Status.IsAnySucceeded)
                throw new UnreachableProxyServerException();
        }

        // Establish first connection and create a session
        State = ClientState.FindingReachableServer;
        var vpnEndPoint = await _serverFinder.FindReachableServerAsync([Token.ServerToken], linkedCts.Token).Vhc();
        var allowRedirect = !_serverFinder.CustomServerEndpoints.Any();
        await ConnectInternal(vpnEndPoint, allowRedirect: allowRedirect, linkedCts.Token).Vhc();
        State = ClientState.Connected;
    }


    private async Task ConnectInternal(VpnEndPoint vpnEndPoint, bool allowRedirect, CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogInformation("Connecting to the server... EndPoint: {hostEndPoint}",
                VhLogger.Format(vpnEndPoint.TcpEndPoint));
            State = ClientState.Connecting;

            // create connector service
            _connectorService = new ConnectorService(
                options: new ConnectorServiceOptions(
                    ProxyEndPointManager: ProxyEndPointManager,
                    SocketFactory: _socketFactory,
                    VpnEndPoint: vpnEndPoint,
                    RequestTimeout: Config.TcpConnectTimeout,
                    AllowTcpReuse: false));

            // send hello request
            var clientInfo = new ClientInfo {
                ClientId = Config.ClientId,
                ClientVersion = Config.Version.ToString(3),
                MinProtocolVersion = _connectorService.ProtocolVersion,
                MaxProtocolVersion = VpnHoodClientConfig.MaxProtocolVersion,
                UserAgent = Config.UserAgent
            };

            var request = new HelloRequest {
                RequestId = UniqueIdFactory.Create(),
                EncryptedClientId = VhUtils.EncryptClientId(clientInfo.ClientId, Token.Secret),
                ClientInfo = clientInfo,
                TokenId = Token.TokenId,
                ServerLocation = _serverFinder.ServerLocation,
                PlanId = Config.PlanId,
                AccessCode = Config.AccessCode,
                AllowRedirect = allowRedirect,
                IsIpV6Supported = _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6),
                UserReview = Config.UserReview
            };

            using var requestResult = await ConnectorService.SendRequest<HelloResponse>(request, cancellationToken).Vhc();
            requestResult.Connection.PreventReuse(); // lets hello request stream not to be reused
            _connectorService.AllowTcpReuse = Config.AllowTcpReuse; // after hello, we can reuse, as the other connections can use websocket

            var helloResponse = requestResult.Response;
            if (helloResponse.ClientPublicAddress is null)
                throw new NotSupportedException($"Server must returns {nameof(helloResponse.ClientPublicAddress)}.");

            // sort out server IncludeIpRanges
            var serverIncludeIpRangesByApp = helloResponse.IncludeIpRanges?.ToOrderedList() ?? IpNetwork.All.ToIpRanges();
            var serverIncludeIpRangesByDevice = helloResponse.VpnAdapterIncludeIpRanges?.ToOrderedList() ?? IpNetwork.All.ToIpRanges();
            var serverIncludeIpRanges = serverIncludeIpRangesByApp.Intersect(serverIncludeIpRangesByDevice);
            var serverAllowedLocalNetworks = IpNetwork.LocalNetworks.ToIpRanges().Intersect(serverIncludeIpRanges);

            // log response
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Hurray! Client has been connected! " +
                $"SessionId: {VhLogger.FormatId(helloResponse.SessionId)}, " +
                $"ServerVersion: {helloResponse.ServerVersion}, " +
                $"ProtocolVersion: {helloResponse.ProtocolVersion}, " +
                $"CurrentProtocolVersion: {_connectorService.ProtocolVersion}, " +
                $"ClientIp: {VhLogger.Format(helloResponse.ClientPublicAddress)}, " +
                $"UdpPort: {helloResponse.UdpPort}, " +
                $"IsTcpPacketSupported: {helloResponse.IsTcpPacketSupported}, " +
                $"IsTcpProxySupported: {helloResponse.IsTcpProxySupported}, " +
                $"IsLocalNetworkAllowed: {serverAllowedLocalNetworks.Any()}, " +
                $"NetworkV4: {helloResponse.VirtualIpNetworkV4}, " +
                $"NetworkV6: {helloResponse.VirtualIpNetworkV6}, " +
                $"ClientCountry: {helloResponse.ClientCountry}");

            // initialize the connector
            _connectorService.Init(
                helloResponse.ProtocolVersion,
                requestTimeout: helloResponse.RequestTimeout.WhenNoDebugger(),
                tcpReuseTimeout: helloResponse.TcpReuseTimeout,
                serverSecret: helloResponse.ServerSecret,
                useWebSocket: Config.UseWebSocket);

            // get session id
            _sessionId = helloResponse.SessionId;
            _sessionKey = helloResponse.SessionKey;

            var hostUdpEndPoint = helloResponse is { UdpPort: > 0, ProtocolVersion: >= 11 }
                ? new IPEndPoint(_connectorService.VpnEndPoint.TcpEndPoint.Address, helloResponse.UdpPort.Value)
                : null;

            // Build session ip ranges
            _staticIpFilter.IncludeRanges = Config.IncludeIpRangesByApp.ToOrderedList();

            // set DNS after setting IpFilters
            var dnsConfig = ClientHelper
                .GetDnsServers(Config.DnsServers,
                serverDnsAddresses: helloResponse.DnsServers ?? [],
                serverIncludeIpRanges: serverIncludeIpRangesByApp,
                ipFilter: _staticIpFilter);

            // Build the IncludeIpRanges for the VpnAdapter
            SessionIncludeIpRangesByDevice = ClientHelper.BuildIncludeIpRangesByDevice(
                includeIpRanges: serverIncludeIpRangesByDevice.Intersect(Config.IncludeIpRangesByDevice),
                canProtectSocket: _vpnAdapter.CanProtectSocket,
                includeLocalNetwork: Config.IncludeLocalNetwork,
                catcherIps: [Config.TcpProxyCatcherAddressIpV4, Config.TcpProxyCatcherAddressIpV6],
                hostIpAddress: _connectorService.VpnEndPoint.TcpEndPoint.Address);

            // add serverIncludeIpRanges to includes after determining DnsServers
            // sometimes packet goes directly to the adapter especially on windows, so we need to make sure filter them
            _staticIpFilter.IncludeRanges = _staticIpFilter.IncludeRanges
                .Intersect(serverIncludeIpRanges)
                .Intersect(SessionIncludeIpRangesByDevice);

            // report Suppressed
            if (helloResponse.SuppressedTo == SessionSuppressType.YourSelf)
                VhLogger.Instance.LogWarning("You suppressed a session of yourself!");

            else if (helloResponse.SuppressedTo == SessionSuppressType.Other)
                VhLogger.Instance.LogWarning("You suppressed a session of another client!");

            // validate channel protocols
            if (hostUdpEndPoint is null)
                VhLogger.Instance.LogWarning("The server does not support UDP channel.");

            if (helloResponse is { IsTcpPacketSupported: false, IsTcpProxySupported: false })
                throw new NotSupportedException(
                    "The server does not support any protocol to support TCP. Please contact support.");

            if (!helloResponse.IsTcpPacketSupported && !Config.IsTcpProxySupported)
                throw new NotSupportedException(
                    "The server does not support any protocol to support your client.");

            if (!helloResponse.IsTcpPacketSupported && !Config.UseTcpProxy)
                VhLogger.Instance.LogWarning("TCP Proxy enabled because the server does not support TCP packets.");

            if (!helloResponse.IsTcpProxySupported && Config.UseTcpProxy)
                VhLogger.Instance.LogWarning("TCP Proxy disabled because the server does not support it.");

            // set the session info
            SessionInfo = new SessionInfo {
                SessionId = helloResponse.SessionId.ToString(),
                ClientPublicIpAddress = helloResponse.ClientPublicAddress,
                ClientCountry = helloResponse.ClientCountry,
                AccessInfo = helloResponse.AccessInfo ?? new AccessInfo(),
                IsLocalNetworkAllowed = serverAllowedLocalNetworks.Any(),
                DnsConfig = dnsConfig,
                IsPremiumSession = helloResponse.AccessUsage?.IsPremium ?? false,
                IsUdpChannelSupported = hostUdpEndPoint != null,
                AccessKey = helloResponse.AccessKey,
                ServerVersion = Version.Parse(helloResponse.ServerVersion),
                SuppressedTo = helloResponse.SuppressedTo,
                AdRequirement = helloResponse.AdRequirement,
                CreatedTime = DateTime.UtcNow,
                IsTcpPacketSupported = helloResponse.IsTcpPacketSupported,
                IsTcpProxySupported = helloResponse.IsTcpProxySupported,
                ChannelProtocols = ChannelProtocolValidator.GetChannelProtocols(helloResponse),
                ServerLocationInfo = helloResponse.ServerLocation != null
                    ? ServerLocationInfo.Parse(helloResponse.ServerLocation)
                    : null
            };

            // usage trackers
            if (Config.AllowAnonymousTracker) {
                // Anonymous server usage tracker
                if (!string.IsNullOrEmpty(helloResponse.GaMeasurementId)) {
                    var ga4Tracking = new Ga4TagTracker {
                        SessionCount = 1,
                        MeasurementId = helloResponse.GaMeasurementId,
                        ClientId = Config.ClientId,
                        SessionId = helloResponse.SessionId.ToString(),
                        UserAgent = Config.UserAgent,
                        UserProperties = new Dictionary<string, object>
                            { { "client_version", Config.Version.ToString(3) } }
                    };

                    _ = ga4Tracking.TryTrack(new Ga4TagEvent { EventName = TrackEventNames.SessionStart },
                        VhLogger.Instance);
                }

                // Anonymous app usage tracker
                if (Tracker != null) {
                    _ = Tracker.TryTrack(
                        ClientTrackerBuilder.BuildConnectionSucceeded(
                            _serverFinder.ServerLocation,
                            isIpV6Supported: _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6),
                            hasRedirected: !allowRedirect,
                            endPoint: _connectorService.VpnEndPoint.TcpEndPoint,
                            adNetworkName: null));
                }
            }

            // prepare packet capture
            // Set a default to capture & drop the packets if the server does not provide a network
            var networkV4 = helloResponse.VirtualIpNetworkV4 ?? new IpNetwork(IPAddress.Parse("10.255.0.2"), 32);
            var networkV6 = helloResponse.VirtualIpNetworkV6 ?? new IpNetwork(IPAddressUtil.GenerateUlaAddress(0x1001), 128);
            VhLogger.Instance.LogInformation(
                "Starting VpnAdapter... DnsServers: {DnsServers}, IncludeNetworks: {longIncludeNetworks}",
                SessionInfo.DnsConfig, VhLogger.Format(SessionIncludeIpRangesByDevice.ToIpNetworks()));

            // wait for ad before adapter
            var passthroughForAd = helloResponse.AdRequirement != AdRequirement.None;
            if (passthroughForAd) {
                _waitForAdCts = new TaskCompletionSource();
                FireStateChanged();
                await _waitForAdCts.Task;
                _waitForAdCts = null;
            }

            // create session
            _session = new ClientSession(
                vpnAdapter: _vpnAdapter,
                socketFactory: _socketFactory,
                tracker: Tracker,
                sessionInfo: SessionInfo,
                sessionId: SessionId,
                sessionKey: SessionKey,
                accessUsage: helloResponse.AccessUsage ?? new AccessUsage(),
                connectorService: _connectorService!,
                domainFilteringService: _domainFilteringService,
                netFilter: _netFilter,
                passthroughForAd: passthroughForAd,
                options: new ClientSessionOptions {
                    ChannelProtocol = ChannelProtocol,
                    TcpProxyCatcherAddressIpV4 = Config.TcpProxyCatcherAddressIpV4,
                    TcpProxyCatcherAddressIpV6 = Config.TcpProxyCatcherAddressIpV6,
                    DropQuic = Config.DropQuic,
                    UseTcpProxy = Config.UseTcpProxy,
                    DropUdp = Config.DropUdp
                },
                config: new ClientSessionConfig {
                    RemoteMtu = helloResponse.Mtu,
                    MaxPacketChannelCount = helloResponse.MaxPacketChannelCount != 0
                        ? Math.Min(Config.MaxPacketChannelCount, helloResponse.MaxPacketChannelCount)
                        : Config.MaxPacketChannelCount,
                    MaxPacketChannelLifespan = Config.MaxPacketChannelLifespan,
                    MinPacketChannelLifespan = Config.MinPacketChannelLifespan,
                    SessionTimeout = Config.SessionTimeout,
                    TcpConnectTimeout = Config.TcpConnectTimeout,
                    StreamProxyBufferSize = Config.StreamProxyBufferSize,
                    UdpProxyBufferSize = Config.UdpProxyBufferSize,
                    UnstableTimeout = Config.UnstableTimeout,
                    AutoWaitTimeout = Config.AutoWaitTimeout,
                    DnsConfig = dnsConfig,
                    IsTcpProxySupported = Config.IsTcpProxySupported,
                    HostUdpEndPoint = hostUdpEndPoint,
                    IsIpV6SupportedByServer = helloResponse.IsIpV6Supported
                });
            _session.StateChanged +=  Session_StateChanged;

            // manage datagram channels
            await _session.ManagePacketChannels(cancellationToken).Vhc();

            // Start the VpnAdapter
            var adapterOptions = new VpnAdapterOptions {
                DnsServers = dnsConfig.DnsServers,
                VirtualIpNetworkV4 = networkV4,
                VirtualIpNetworkV6 = networkV6,
                Mtu = helloResponse.Mtu - TunnelDefaults.MtuOverhead,
                IncludeNetworks = SessionIncludeIpRangesByDevice.ToIpNetworks(),
                SessionName = Config.SessionName,
                ExcludeApps = Config.ExcludeApps,
                IncludeApps = Config.IncludeApps
            };

            // start the VpnAdapter
            await _vpnAdapter.Start(adapterOptions, cancellationToken);
        }
        catch (TimeoutException) {
            throw new ConnectionTimeoutException("Could not connect to the server in the given time.");
        }
        catch (RedirectHostException ex) {
            if (!allowRedirect) {
                VhLogger.Instance.LogError(ex,
                    "The server replies with a redirect to another server again. We already redirected earlier. This is unexpected.");
                throw;
            }

            // init new connector
            _connectorService?.Dispose();
            State = ClientState.FindingBestServer;
            var redirectServerTokens = ex.RedirectServerTokens;

            // legacy: convert the redirect host endpoints to a new server token using initial server token
#pragma warning disable CS0618 // Type or member is obsolete
            if (redirectServerTokens is null) {
                var serverToken = JsonUtils.JsonClone(Token.ServerToken);
                serverToken.HostEndPoints = ex.RedirectHostEndPoints;
                redirectServerTokens = [serverToken];
            }
#pragma warning restore CS0618 // Type or member is obsolete

            // find the best redirected server
            var redirectedEndPoint = await _serverFinder
                .FindBestRedirectedServerAsync(redirectServerTokens, cancellationToken).Vhc();
            await ConnectInternal(redirectedEndPoint, false, cancellationToken).Vhc();
        }
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

    public void SetWaitForAd()
    {
        _session?.PassthroughForAd = true;
        FireStateChanged();
    }

    public Task SetAdFailed(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        // if there is no wait for ad, then we should remove passthrough flag and resume the connection
        // App is responsible to disconnect for failed ad
        if (_waitForAdCts == null)
            _session?.PassthroughForAd = false;

        // first step should always be accepted to jump to the next step
        _waitForAdCts?.TrySetResult();
        FireStateChanged();
        return Task.CompletedTask;
    }

    public Task SetAdOk(CancellationToken cancellationToken)
    {
        // make everything is ok. 
        _session?.PassthroughForAd = false;
        _session?.DropCurrentConnections();
        _waitForAdCts?.TrySetResult();
        FireStateChanged();
        return Task.CompletedTask;
    }

    public async Task SetRewardedAdOk(string adData, CancellationToken cancellationToken)
    {
        if (_session != null)
            await _session.SendRewardedAdData(adData, cancellationToken);

        await SetAdOk(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session != null)
            await _session.DisposeAsync();

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
        _waitForAdCts?.TrySetCanceled();

        // close session
        _session?.Dispose();
        _session?.StateChanged -= Session_StateChanged;

        _netFilter.Dispose();

        // dispose ConnectorService before ProxyEndPointManager as it uses ProxyEndPointManager
        VhLogger.Instance.LogDebug("Disposing ConnectorService...");
        _connectorService?.Dispose();

        // dispose ProxyEndPointManager before adapter get closed and it needs Adapter's SocketFactory
        VhLogger.Instance.LogDebug("Disposing ProxyEndPointManager...");
        ProxyEndPointManager.Dispose();

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
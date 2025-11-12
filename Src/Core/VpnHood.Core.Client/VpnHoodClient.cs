using System.Diagnostics;
using System.Net;
using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.DomainFiltering;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Trackers;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Monitoring;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

public class VpnHoodClient : IDisposable, IAsyncDisposable
{
    private bool _disposedInternal;
    private bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ProxyManager _proxyManager;
    private readonly Dictionary<IPAddress, bool> _includeIps = new();
    private readonly IVpnAdapter _vpnAdapter;
    private readonly ClientHost _clientHost;
    private ClientUsageTracker? _clientUsageTracker;
    private DateTime? _initConnectedTime;
    private DateTime? _lastConnectionErrorTime;
    private byte[]? _sessionKey;
    private ClientState _lastState = ClientState.None;
    private readonly Lock _stateEventLock = new();
    private ConnectorService? _connectorService;
    private DateTime? _autoWaitTime;
    private readonly ServerFinder _serverFinder;
    private bool _isDnsServersAccepted;
    private ulong? _sessionId;
    private ClientSessionStatus? _sessionStatus;
    private IPAddress[] _dnsServers;
    private int _sessionPacketChannelCount;
    private readonly AsyncLock _packetChannelLock = new();
    private readonly AsyncLock _disposeLock = new();
    private readonly Job _cleanupJob;
    private readonly Tunnel _tunnel;
    private TaskCompletionSource? _waitForAdCts;
    private bool _isPassthroughForAd;
    private bool _isDnsOverTlsDetected;

    private ConnectorService ConnectorService => VhUtils.GetRequiredInstance(_connectorService);

    public event EventHandler? StateChanged;
    public Token Token { get; }
    public VpnHoodClientConfig Config { get; }
    public DomainFilterService DomainFilterService { get; }
    public ProxyEndPointManager ProxyEndPointManager { get; }
    public ISocketFactory SocketFactory { get; }
    public ISessionStatus? SessionStatus => _sessionStatus;
    public ITracker? Tracker { get; }
    public bool IsIpV6SupportedByServer { get; private set; }
    public bool IsIpV6SupportedByClient { get; internal set; }
    public IpRangeOrderedList IncludeIpRanges { get; private set; }
    public IpRangeOrderedList VpnAdapterIncludeIpRanges { get; private set; }
    public IPEndPoint? HostTcpEndPoint => _connectorService?.EndPointInfo.TcpEndPoint;
    public IPEndPoint? HostUdpEndPoint { get; private set; }

    public byte[] SessionKey =>
        _sessionKey ?? throw new InvalidOperationException($"{nameof(SessionKey)} has not been initialized.");

    public byte[]? ServerSecret { get; private set; }
    public ulong SessionId => _sessionId ?? throw new InvalidOperationException("SessionId has not been initialized.");
    public SessionInfo? SessionInfo { get; private set; }
    public Exception? LastException { get; private set; }
    public DateTime StateChangedTime { get; private set; } = DateTime.Now;
    private ChannelProtocol _channelProtocol;

    public VpnHoodClient(IVpnAdapter vpnAdapter,
        ISocketFactory socketFactory,
        string? storageFolder,
        ITracker? tracker,
        ClientOptions options)
    {
        if (options.TcpProxyCatcherAddressIpV4 == null)
            throw new ArgumentNullException(nameof(options.TcpProxyCatcherAddressIpV4));

        if (options.TcpProxyCatcherAddressIpV6 == null)
            throw new ArgumentNullException(nameof(options.TcpProxyCatcherAddressIpV6));

        if (!VhUtils.IsInfinite(options.MaxPacketChannelTimespan) &&
            options.MaxPacketChannelTimespan < options.MinPacketChannelTimespan)
            throw new ArgumentNullException(nameof(options.MaxPacketChannelTimespan),
                $"{nameof(options.MaxPacketChannelTimespan)} must be bigger or equal than {nameof(options.MinPacketChannelTimespan)}.");

        if (string.IsNullOrEmpty(storageFolder))
            storageFolder = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "vpn-service");

        Config = new VpnHoodClientConfig {
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
            UserReview = options.UserReview
        };

        Token = Token.FromAccessKey(options.AccessKey);
        socketFactory = new AdapterSocketFactory(vpnAdapter, socketFactory);
        SocketFactory = socketFactory;
        Tracker = tracker;
        _dnsServers = options.DnsServers ?? [];
        _vpnAdapter = vpnAdapter;
        _channelProtocol = options.ChannelProtocol;
        ProxyEndPointManager = new ProxyEndPointManager(
            proxyOptions: options.ProxyOptions ?? new ProxyOptions(),
            storagePath: Path.Combine(storageFolder, "proxies"),
            socketFactory: socketFactory,
            serverCheckTimeout: options.ServerQueryTimeout);

        _serverFinder = new ServerFinder(socketFactory, Token.ServerToken,
            serverLocation: options.ServerLocation,
            serverQueryTimeout: options.ServerQueryTimeout,
            endPointStrategy: options.EndPointStrategy != EndPointStrategy.Auto
                ? options.EndPointStrategy
                : Token.ServerToken.EndPointsStrategy,
            customServerEndpoints: options.CustomServerEndpoints ?? [],
            tracker: options.AllowEndPointTracker ? tracker : null,
            proxyEndPointManager: ProxyEndPointManager);

        _proxyManager = new ProxyManager(socketFactory, new ProxyManagerOptions {
            IsPingSupported = false,
            PacketProxyCallbacks = null,
            UdpTimeout = TunnelDefaults.UdpTimeout,
            MaxUdpClientCount = TunnelDefaults.MaxUdpClientCount,
            MaxPingClientCount = TunnelDefaults.MaxPingClientCount,
            PacketQueueCapacity = TunnelDefaults.ProxyPacketQueueCapacity,
            IcmpTimeout = TunnelDefaults.IcmpTimeout,
            UdpBufferSize = options.UdpProxyBufferSize ?? TunnelDefaults.ClientUdpProxyBufferSize,
            LogScope = null,
            UseUdpProxy2 = true,
            AutoDisposePackets = true
        });

        _proxyManager.PacketReceived += Proxy_PacketReceived;

        var dnsRange = options.DnsServers?.Select(x => new IpRange(x)).ToArray() ?? [];
        VpnAdapterIncludeIpRanges = options.VpnAdapterIncludeIpRanges.ToOrderedList().Union(dnsRange);
        IncludeIpRanges = options.IncludeIpRanges.ToOrderedList().Union(dnsRange);

        // SNI is sensitive, must be explicitly enabled
        DomainFilterService = new DomainFilterService(options.DomainFilter, forceLogSni: options.ForceLogSni);

        // Tunnel
        _tunnel = new Tunnel(new TunnelOptions {
            AutoDisposePackets = true,
            PacketQueueCapacity = TunnelDefaults.TunnelPacketQueueCapacity,
            MaxPacketChannelCount = TunnelDefaults.MaxPacketChannelCount,
            UseSpeedometerTimer = true
        });
        _tunnel.PacketReceived += Tunnel_PacketReceived;

        // create proxy host
        _clientHost = new ClientHost(
            this,
            domainFilterService: DomainFilterService,
            tunnel: _tunnel,
            catcherAddressIpV4: options.TcpProxyCatcherAddressIpV4,
            catcherAddressIpV6: options.TcpProxyCatcherAddressIpV6,
            streamProxyBufferSize: options.StreamProxySendBufferSize ?? TunnelDefaults.ClientStreamProxyBufferSize);

        _clientHost.PacketReceived += ClientHost_PacketReceived;

        // init vpnAdapter events
        vpnAdapter.Disposed += (_, _) => _ = DisposeAsync();
        vpnAdapter.PacketReceived += VpnAdapter_PacketReceived;

        // Create simple disposable objects
        _cancellationTokenSource = new CancellationTokenSource();
        _cleanupJob = new Job(Cleanup, nameof(VpnHoodClient));
    }

    public ChannelProtocol ChannelProtocol {
        get => _channelProtocol;
        set {
            value = ChannelProtocolValidator.Validate(value, SessionInfo);
            if (_channelProtocol == value) return;

            // check is udp changed
            var isUdpChanged = _channelProtocol == ChannelProtocol.Udp && value != ChannelProtocol.Udp;
            _channelProtocol = value;

            // reload if udp changed
            VhLogger.Instance.LogInformation("VpnProtocol is changed to {VpnProtocol}.", _channelProtocol);
            if (isUdpChanged) {
                _tunnel.MaxPacketChannelCount =
                    _channelProtocol == ChannelProtocol.Udp ? 1 : Config.MaxPacketChannelCount;
                _tunnel.RemoveAllPacketChannels();
                Task.Run(() => ManagePacketChannels(_cancellationTokenSource.Token));
            }
        }
    }

    private bool IsTcpProxy {
        get {
            // client does not support tcp proxy
            if (!Config.IsTcpProxySupported)
                return false;

            // server does not support tcp proxy
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (SessionInfo is { IsTcpProxySupported: false })
                return false;

            // server does not support tcp packets so only tcp proxy can work
            if (SessionInfo is { IsTcpPacketSupported: false })
                return true;

            // follow config
            return Config.UseTcpProxy;
        }
    }

    // DropQUIC is useless if not tcp proxy
    private bool IsDropQuic => Config.DropQuic && IsTcpProxy;


    public ClientState State {
        get {
            if (field is ClientState.Disconnecting or ClientState.Disposed)
                return field;

            // waiting for ad
            if (_waitForAdCts?.Task.IsCompleted is false)
                return ClientState.WaitingForAd;

            // waiting 
            if (_isPassthroughForAd && field == ClientState.Connected)
                return ClientState.WaitingForAdEx;

            return field;
        }
        private set {
            field = value;
            FireStateChanged();
        }
    } = ClientState.None;

    public ProgressStatus? StateProgress =>
        State switch {
            ClientState.FindingReachableServer or ClientState.FindingBestServer => _serverFinder.Progress,
            ClientState.ValidatingProxies => ProxyEndPointManager.Progress,
            _ => null
        };

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

    internal async Task AddPassthruTcpStream(IClientStream orgTcpClientStream, IPEndPoint hostEndPoint,
        string channelId, byte[] initBuffer, CancellationToken cancellationToken)
    {
        // set timeout
        using var timeoutCts = new CancellationTokenSource(ConnectorService.RequestTimeout);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        // connect to host
        var tcpClient = SocketFactory.CreateTcpClient(hostEndPoint);
        await tcpClient.ConnectAsync(hostEndPoint.Address, hostEndPoint.Port, connectCts.Token).Vhc();

        // create and add the channel
        var channel = new ProxyChannel(channelId, orgTcpClientStream,
            new TcpClientStream(tcpClient, tcpClient.GetStream(), channelId + ":host"),
            _clientHost.StreamProxyBufferSize);

        // flush initBuffer
        await tcpClient.GetStream().WriteAsync(initBuffer, connectCts.Token);

        try {
            _proxyManager.AddChannel(channel);
        }
        catch {
            channel.Dispose();
            throw;
        }
    }

    public async Task Connect(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        // Connect
        try {
            // merge cancellation tokens
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);

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
            IsIpV6SupportedByClient = await IPAddressUtil.IsIpv6Supported();
            _proxyManager.IsIpV6Supported = IsIpV6SupportedByClient;
            _serverFinder.IncludeIpV6 = IsIpV6SupportedByClient;
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            VhLogger.Instance.LogInformation(
                "DropUdp: {DropUdp}, VpnProtocol: {VpnProtocol}, " +
                "EndPointStrategy: {EndPointStrategy}" +
                "IncludeLocalNetwork: {IncludeLocalNetwork}, MinWorkerThreads: {WorkerThreads}, " +
                "CompletionPortThreads: {CompletionPortThreads}, ClientIpV6: {ClientIpV6}, ProcessId: {ProcessId}",
                Config.DropUdp, _channelProtocol, _serverFinder.EndPointStrategy,
                Config.IncludeLocalNetwork, workerThreads, completionPortThreads, IsIpV6SupportedByClient,
                Process.GetCurrentProcess().Id);

            // report version
            VhLogger.Instance.LogInformation(
                "ClientVersion: {ClientVersion}, " +
                "ClientMinProtocolVersion: {ClientMinProtocolVersion}, ClientMaxProtocolVersion: {ClientMaxProtocolVersion}, " +
                "ClientId: {ClientId}",
                Config.Version, Config.MinProtocolVersion, Config.MaxProtocolVersion,
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
            var hostEndPoint = await _serverFinder.FindReachableServerAsync(linkedCts.Token).Vhc();
            var allowRedirect = !_serverFinder.CustomServerEndpoints.Any();
            await ConnectInternal(hostEndPoint, allowRedirect: allowRedirect, linkedCts.Token).Vhc();

            State = ClientState.Connected;
            _initConnectedTime = DateTime.UtcNow;
        }
        catch (Exception ex) {
            // clear before start new async task
            await DisposeAsync(ex);
            throw;
        }
    }

    private IpRangeOrderedList BuildVpnAdapterIncludeIpRanges(IPAddress hostIpAddress)
    {
        // Start with user VpnAdapterIncludeIpRanges
        var includeIpRanges = VpnAdapterIncludeIpRanges;

        // exclude server if ProtectClient is not supported to prevent loop
        if (!_vpnAdapter.CanProtectSocket)
            includeIpRanges = includeIpRanges.Exclude(hostIpAddress);

        // exclude local networks
        if (!Config.IncludeLocalNetwork)
            includeIpRanges = includeIpRanges.Exclude(IpNetwork.LocalNetworks.ToIpRanges());

        // exclude multicast and broadcast
        includeIpRanges = includeIpRanges
            .Exclude(IpNetwork.MulticastNetworks.ToIpRanges())
            .Exclude(IPAddress.Broadcast);

        // Make sure CatcherAddress is included
        includeIpRanges = includeIpRanges.Union([
            new IpRange(_clientHost.CatcherAddressIpV4),
            new IpRange(_clientHost.CatcherAddressIpV6)
        ]);

        return includeIpRanges; //sort and unify
    }

    // WARNING: Performance Critical!
    private void ClientHost_PacketReceived(object? sender, IpPacket ipPacket)
    {
        _vpnAdapter.SendPacketQueued(ipPacket);
    }

    // WARNING: Performance Critical!
    private void Proxy_PacketReceived(object? sender, IpPacket ipPacket)
    {
        _vpnAdapter.SendPacketQueued(ipPacket);
    }

    // WARNING: Performance Critical!
    private void Tunnel_PacketReceived(object? sender, IpPacket ipPacket)
    {
        _vpnAdapter.SendPacketQueued(ipPacket);
    }

    // WARNING: Performance Critical! Mango Section
    private void VpnAdapter_PacketReceived(object? sender, IpPacket ipPacket)
    {
        // stop traffic if the client has been disposed
        if (_disposed || _initConnectedTime is null)
            return;

        // stop traffic if the client is paused and unpause after AutoPauseTimeout
        if (_autoWaitTime != null) {
            if (FastDateTime.Now - _autoWaitTime.Value < Config.AutoWaitTimeout)
                throw new PacketDropException("Connection is paused. The packet has been dropped.");

            // resume connection if the client is paused and AutoWaitTimeout is not set
            _autoWaitTime = null;
            State = ClientState.Unstable;
        }

        // Manage datagram channels if needed
        if (ShouldManagePacketChannels && !_packetChannelLock.IsLocked)
            _ = ManagePacketChannels(_cancellationTokenSource.Token);

        // Multicast packets are not supported (Already excluded by adapter filter)
        if (ipPacket.IsMulticast()) {
            PacketLogger.LogPacket(ipPacket, "A multicast packet has been dropped.");
            ipPacket.Dispose();
            return;
        }

        // Broadcast packets are not supported (Already excluded by adapter filter)
        if (ipPacket.IsBroadcast()) {
            PacketLogger.LogPacket(ipPacket, "A broad packet has been dropped.");
            ipPacket.Dispose();
            return;
        }

        // TcpHost has to manage its own packets
        if (_clientHost.IsOwnPacket(ipPacket)) {
            _clientHost.ProcessOutgoingPacket(ipPacket);
            return;
        }

        // tcp already check for InInRange and IpV6 and Proxy
        if (ipPacket.Protocol == IpProtocol.Tcp) {
            var tcpPacket = ipPacket.ExtractTcp();
            _isDnsOverTlsDetected |= tcpPacket.DestinationPort == 853;
            if (IsTcpProxy || !IsInEpRange(ipPacket))
                _clientHost.ProcessOutgoingPacket(ipPacket);
            else
                _tunnel.SendPacketQueuedAsync(ipPacket).VhBlock();
            return;
        }

        // use local proxy if the packet is not in the range and not ICMP.
        // ICMP is not supported by the local proxy for split tunnel
        if (!IsInEpRange(ipPacket) && !ipPacket.IsIcmpEcho()) {
            _proxyManager.SendPacketQueued(ipPacket);
            return;
        }

        // Drop IPv6 if not support
        if (ipPacket.IsV6() && !IsIpV6SupportedByServer)
            throw new PacketDropException("IPv6 packet has been dropped because server does not support IPv6.");

        // ICMP packet must go through tunnel because PingProxy does not support protect socket
        if (ipPacket.IsIcmpEcho()) {
            // ICMP can not be proxied so we don't need to check InRange
            _tunnel.SendPacketQueuedAsync(ipPacket).VhBlock();
            return;
        }

        // Udp
        if (ipPacket.Protocol == IpProtocol.Udp && ShouldTunnelUdpPacket(ipPacket.ExtractUdp())) {
            _tunnel.SendPacketQueuedAsync(ipPacket).VhBlock();
            return;
        }

        // Drop packet
        throw new PacketDropException("Packet has been dropped because no one handle it.");
    }

    private bool ShouldTunnelUdpPacket(UdpPacket udpPacket)
    {
        if (Config.DropUdp)
            return false;

        if (udpPacket.DestinationPort is 80 or 443 && IsDropQuic)
            return false;

        return true;
    }

    public bool IsInEpRange(IpPacket ipPacket)
    {
        var destinationPort = 0;
        if (ipPacket.Protocol == IpProtocol.Tcp) destinationPort = ipPacket.ExtractTcp().DestinationPort;
        if (ipPacket.Protocol == IpProtocol.Udp) destinationPort = ipPacket.ExtractUdp().DestinationPort;
        return IsInEpRange(ipPacket.DestinationAddress, destinationPort);
    }

    public bool IsInEpRange(IPAddress ipAddress, int port)
    {
        if (_isPassthroughForAd)
            return port is 53 or 853;

        return IsInIpRange(ipAddress);
    }

    public bool IsInIpRange(IPAddress ipAddress)
    {
        // only dns servers are tunneled if IsWaitingForAd is set
        if (_isPassthroughForAd)
            return _dnsServers.Contains(ipAddress);

        // all IPs are included if there is no filter
        if (IncludeIpRanges.Count == 0)
            return true;

        // check tcp-loopback
        if (ipAddress.Equals(_clientHost.CatcherAddressIpV4) ||
            ipAddress.Equals(_clientHost.CatcherAddressIpV6))
            return true;

        // check the cache
        if (_includeIps.TryGetValue(ipAddress, out var isInRange))
            return isInRange;

        // check include
        isInRange = IncludeIpRanges.IsInRange(ipAddress);

        // cache the result
        // we don't need to keep that much ips in the cache
        if (_includeIps.Count > 0xFFFF) {
            VhLogger.Instance.LogInformation("Clearing IP filter cache!");
            _includeIps.Clear();
        }

        _includeIps.Add(ipAddress, isInRange);
        return isInRange;
    }

    private bool ShouldManagePacketChannels =>
        _tunnel.PacketChannelCount < _tunnel.MaxPacketChannelCount;

    private async ValueTask ManagePacketChannels(CancellationToken cancellationToken)
    {
        // if the lock is not acquired, it means that another thread is already managing packet channels
        using var lockResult = await _packetChannelLock.LockAsync(TimeSpan.Zero, cancellationToken);
        if (!lockResult.Succeeded)
            return;

        try {
            // check is adding channels allowed
            if (!ShouldManagePacketChannels)
                return;

            // make sure only one UdpChannel exists for PacketChannels if UseUdpChannel is on
            if (ChannelProtocol == ChannelProtocol.Udp)
                AddUdpChannel();
            else
                await AddTcpPacketChannel(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            if (!_disposed)
                VhLogger.LogError(GeneralEventId.PacketChannel, ex, "Could not Manage PacketChannels.");
        }
    }

    private void AddUdpChannel()
    {
        if (VhUtils.IsNullOrEmpty(ServerSecret)) throw new Exception("ServerSecret has not been set.");
        if (VhUtils.IsNullOrEmpty(_sessionKey)) throw new Exception("Server UdpKey has not been set.");
        if (HostUdpEndPoint == null) throw new Exception("Server does not serve any UDP endpoint.");

        UdpChannel? udpChannel = null;
        try {
            udpChannel = ClientUdpChannelFactory.Create(
                new ClientUdpChannelOptions {
                    SocketFactory = SocketFactory,
                    ServerKey = ServerSecret,
                    RemoteEndPoint = HostUdpEndPoint,
                    SessionKey = _sessionKey,
                    SessionId = SessionId,
                    ProtocolVersion = ConnectorService.ProtocolVersion,
                    AutoDisposePackets = true,
                    Blocking = true,
                    ChannelId = Guid.NewGuid().ToString(),
                    Lifespan = null,
                    BufferSize = TunnelDefaults.ClientUdpChannelBufferSize
                });
            _tunnel.AddChannel(udpChannel);
        }
        catch {
            udpChannel?.Dispose();
            throw;
        }
    }

    private async Task ConnectInternal(IPEndPoint hostEndPoint, bool allowRedirect, CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogInformation("Connecting to the server... EndPoint: {hostEndPoint}",
                VhLogger.Format(hostEndPoint));
            State = ClientState.Connecting;

            // create connector service
            _connectorService = new ConnectorService(
                new ConnectorEndPointInfo {
                    ProxyEndPointManager = ProxyEndPointManager,
                    HostName = Token.ServerToken.HostName,
                    TcpEndPoint = hostEndPoint,
                    CertificateHash = Token.ServerToken.CertificateHash
                },
                socketFactory: SocketFactory,
                requestTimeout: Config.TcpConnectTimeout,
                allowTcpReuse: false);

            // send hello request
            var clientInfo = new ClientInfo {
                ClientId = Config.ClientId,
                ClientVersion = Config.Version.ToString(3),
                MinProtocolVersion = _connectorService.ProtocolVersion,
                MaxProtocolVersion = Config.MaxProtocolVersion,
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
                IsIpV6Supported = IsIpV6SupportedByClient,
                UserReview = Config.UserReview
            };

            using var requestResult = await SendRequest<HelloResponse>(request, cancellationToken).Vhc();
            requestResult.ClientStream.PreventReuse(); // lets hello request stream not to be reused
            _connectorService.AllowTcpReuse =
                Config.AllowTcpReuse; // after hello, we can reuse, as the other connections can use websocket

            var helloResponse = requestResult.Response;
            if (helloResponse.ClientPublicAddress is null)
                throw new NotSupportedException($"Server must returns {nameof(helloResponse.ClientPublicAddress)}.");

            // sort out server IncludeIpRanges
            var serverIncludeIpRanges = helloResponse.IncludeIpRanges?.ToOrderedList();
            var serverVpnAdapterIncludeIpRanges = helloResponse.VpnAdapterIncludeIpRanges?.ToOrderedList();

            // build allowed local networks
            var allowedLocalNetworks = IpNetwork.LocalNetworks.ToIpRanges();
            if (serverIncludeIpRanges != null)
                allowedLocalNetworks = allowedLocalNetworks.Intersect(serverIncludeIpRanges);
            if (serverVpnAdapterIncludeIpRanges != null)
                allowedLocalNetworks = allowedLocalNetworks.Intersect(serverVpnAdapterIncludeIpRanges);

            // log response
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Hurray! Client has been connected! " +
                $"SessionId: {VhLogger.FormatId(helloResponse.SessionId)}, " +
                $"ServerVersion: {helloResponse.ServerVersion}, " +
                $"ProtocolVersion: {helloResponse.ProtocolVersion}, " +
                $"CurrentProtocolVersion: {_connectorService.ProtocolVersion}, " +
                $"ClientIp: {VhLogger.Format(helloResponse.ClientPublicAddress)}, " +
                $"UdpChannelPort: {helloResponse.UdpPort}, " +
                $"IsTcpPacketSupported: {helloResponse.IsTcpPacketSupported}, " +
                $"IsTcpProxySupported: {helloResponse.IsTcpProxySupported}, " +
                $"IsLocalNetworkAllowed: {allowedLocalNetworks.Any()}, " +
                $"NetworkV4: {helloResponse.VirtualIpNetworkV4}, " +
                $"NetworkV6: {helloResponse.VirtualIpNetworkV6}, " +
                $"ClientCountry: {helloResponse.ClientCountry}");

            // initialize the connector
            _connectorService.Init(
                helloResponse.ProtocolVersion,
                requestTimeout: helloResponse.RequestTimeout.WhenNoDebugger(),
                tcpReuseTimeout: helloResponse.TcpReuseTimeout,
                serverSecret: helloResponse.ServerSecret,
                useWebSocket: true);

            // get session id
            _sessionId = helloResponse.SessionId;
            _sessionKey = helloResponse.SessionKey;
            ServerSecret = helloResponse.ServerSecret;
            IsIpV6SupportedByServer = helloResponse.IsIpV6Supported;

            if (helloResponse.UdpPort > 0)
                HostUdpEndPoint = new IPEndPoint(_connectorService.EndPointInfo.TcpEndPoint.Address,
                    helloResponse.UdpPort.Value);

            // VpnAdapterIpRanges
            if (!VhUtils.IsNullOrEmpty(serverVpnAdapterIncludeIpRanges))
                VpnAdapterIncludeIpRanges = VpnAdapterIncludeIpRanges.Intersect(serverVpnAdapterIncludeIpRanges);

            // IncludeIpRanges
            if (serverIncludeIpRanges?.IsAll() is false)
                IncludeIpRanges = IncludeIpRanges.Intersect(serverIncludeIpRanges);

            // set DNS after setting IpFilters
            VhLogger.Instance.LogInformation("Configuring Client DNS servers... DnsServers: {DnsServers}",
                string.Join(", ", _dnsServers.Select(x => x.ToString())));
            _isDnsServersAccepted =
                VhUtils.IsNullOrEmpty(_dnsServers) || _dnsServers.Any(IsInIpRange); // no servers means accept default
            if (!_isDnsServersAccepted)
                VhLogger.Instance.LogWarning(
                    "Client DNS servers have been ignored because the server does not route them.");

            _dnsServers = _dnsServers.Where(IsInIpRange).ToArray();
            if (VhUtils.IsNullOrEmpty(_dnsServers)) {
                _dnsServers = VhUtils.IsNullOrEmpty(helloResponse.DnsServers)
                    ? IPAddressUtil.GoogleDnsServers
                    : helloResponse.DnsServers;
                IncludeIpRanges = IncludeIpRanges.Union(_dnsServers.Select(IpRange.FromIpAddress));
            }

            if (VhUtils.IsNullOrEmpty(_dnsServers?.Where(IsInIpRange)
                    .ToArray())) // make sure there is at least one DNS server
                throw new Exception("Could not specify any DNS server. The server is not configured properly.");

            VhLogger.Instance.LogInformation("DnsServers: {DnsServers}",
                string.Join(", ", _dnsServers.Select(VhLogger.Format)));

            // report Suppressed
            if (helloResponse.SuppressedTo == SessionSuppressType.YourSelf)
                VhLogger.Instance.LogWarning("You suppressed a session of yourself!");

            else if (helloResponse.SuppressedTo == SessionSuppressType.Other)
                VhLogger.Instance.LogWarning("You suppressed a session of another client!");

            // validate channel protocols
            if (HostUdpEndPoint is null)
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
                IsDnsServersAccepted = _isDnsServersAccepted,
                IsLocalNetworkAllowed = allowedLocalNetworks.Any(),
                DnsServers = _dnsServers,
                IsPremiumSession = helloResponse.AccessUsage?.IsPremium ?? false,
                IsUdpChannelSupported = HostUdpEndPoint != null,
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

            // set session status
            _sessionStatus = new ClientSessionStatus(this, helloResponse.AccessUsage ?? new AccessUsage());
            _channelProtocol = ChannelProtocolValidator.Validate(_channelProtocol, SessionInfo);

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
                            isIpV6Supported: IsIpV6SupportedByClient,
                            hasRedirected: !allowRedirect,
                            endPoint: _connectorService.EndPointInfo.TcpEndPoint,
                            adNetworkName: null));

                    _clientUsageTracker = new ClientUsageTracker(_sessionStatus, Tracker);
                }
            }

            // Preparing tunnel
            VhLogger.Instance.LogInformation("Configuring Packet Channels... VpnProtocol: {VpnProtocol}",
                ChannelProtocol);
            _tunnel.RemoteMtu = helloResponse.Mtu;
            _tunnel.MaxPacketChannelCount = helloResponse.MaxPacketChannelCount != 0
                ? _tunnel.MaxPacketChannelCount =
                    Math.Min(Config.MaxPacketChannelCount, helloResponse.MaxPacketChannelCount)
                : Config.MaxPacketChannelCount;

            // manage datagram channels
            await ManagePacketChannels(cancellationToken).Vhc();

            // prepare packet capture
            // Set a default to capture & drop the packets if the server does not provide a network
            var networkV4 = helloResponse.VirtualIpNetworkV4 ?? new IpNetwork(IPAddress.Parse("10.255.0.2"), 32);
            var networkV6 = helloResponse.VirtualIpNetworkV6 ??
                            new IpNetwork(IPAddressUtil.GenerateUlaAddress(0x1001), 128);
            var longIncludeNetworks =
                string.Join(", ", VpnAdapterIncludeIpRanges.ToIpNetworks().Select(VhLogger.Format));
            VhLogger.Instance.LogInformation(
                "Starting VpnAdapter... DnsServers: {DnsServers}, IncludeNetworks: {longIncludeNetworks}",
                SessionInfo.DnsServers, longIncludeNetworks);

            // Build the IncludeIpRanges for the VpnAdapter
            var adapterIncludeRanges =
                BuildVpnAdapterIncludeIpRanges(_connectorService.EndPointInfo.TcpEndPoint.Address);

            // sometimes packet goes directly to the adapter especially on windows, so we need to filter them
            IncludeIpRanges = IncludeIpRanges.Intersect(adapterIncludeRanges);

            // wait for ad before adapter
            if (helloResponse.AdRequirement != AdRequirement.None) {
                _isPassthroughForAd = true; // set passthrough mode
                _waitForAdCts = new TaskCompletionSource();
                FireStateChanged();
                await _waitForAdCts.Task;
                _waitForAdCts = null;
            }

            // Create Tcp Proxy Host
            _clientHost.Start();

            // Start the VpnAdapter
            var adapterOptions = new VpnAdapterOptions {
                DnsServers = SessionInfo.DnsServers,
                VirtualIpNetworkV4 = networkV4,
                VirtualIpNetworkV6 = networkV6,
                Mtu = helloResponse.Mtu - TunnelDefaults.MtuOverhead,
                IncludeNetworks = adapterIncludeRanges.ToIpNetworks().ToArray(),
                SessionName = Config.SessionName,
                ExcludeApps = Config.ExcludeApps,
                IncludeApps = Config.IncludeApps
            };

            // start the VpnAdapter
            await _vpnAdapter.Start(adapterOptions, cancellationToken);
        }
        catch (TimeoutException) {
            // if the connection is timed out, throw connection timeout exception in this case
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
            var redirectedEndPoint =
                await _serverFinder.FindBestRedirectedServerAsync(ex.RedirectHostEndPoints.ToArray(),
                    cancellationToken);
            await ConnectInternal(redirectedEndPoint, false, cancellationToken).Vhc();
        }
    }


    private async Task AddTcpPacketChannel(CancellationToken cancellationToken)
    {
        // Create and send the Request Message
        var request = new TcpPacketChannelRequest {
            RequestId = UniqueIdFactory.Create(),
            SessionId = SessionId,
            SessionKey = SessionKey
        };

        var requestResult = await SendRequest<SessionResponse>(request, cancellationToken).Vhc();
        StreamPacketChannel? channel = null;
        try {
            // find timespan
            var lifespan = VhUtils.IsInfinite(Config.MaxPacketChannelLifespan)
                ? (TimeSpan?)null
                : TimeSpan.FromSeconds(new Random().Next((int)Config.MinPacketChannelLifespan.TotalSeconds,
                    (int)Config.MaxPacketChannelLifespan.TotalSeconds));

            // PacketChannel should not be reused, otherwise its timespan will be meaningless
            if (lifespan != null)
                requestResult.ClientStream.PreventReuse();

            // add the new channel
            channel = new StreamPacketChannel(new StreamPacketChannelOptions {
                ClientStream = requestResult.ClientStream,
                BufferSize = TunnelDefaults.ClientStreamPacketBufferSize,
                ChannelId = request.RequestId,
                Blocking = true,
                AutoDisposePackets = true,
                Lifespan = lifespan
            });
            _tunnel.AddChannel(channel);
            _sessionPacketChannelCount++;
        }
        catch {
            channel?.Dispose();
            requestResult.Dispose();
            throw;
        }
    }

    internal async Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequest request,
        CancellationToken cancellationToken)
        where T : SessionResponse
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        try {
            // create a connection and send the request 
            var requestResult = await ConnectorService.SendRequest<T>(request, cancellationToken).Vhc();
            _sessionStatus?.Update(requestResult.Response.AccessUsage);

            // client is disposed meanwhile
            if (_disposed) {
                requestResult.Dispose();
                throw new ObjectDisposedException(VhLogger.FormatType(this));
            }

            _lastConnectionErrorTime = null;
            if (SessionInfo != null && _vpnAdapter.IsStarted) {
                State = ClientState.Connected; // stable state
            }

            return requestResult;
        }
        catch (SessionException ex) {
            _sessionStatus?.Update(ex.SessionResponse.AccessUsage);

            // SessionException means that the request accepted by server but there is an error for that request
            _lastConnectionErrorTime = null;

            // close session if server has ended the session
            if (ex.SessionResponse.ErrorCode != SessionErrorCode.GeneralError &&
                ex.SessionResponse.ErrorCode != SessionErrorCode.RedirectHost &&
                ex.SessionResponse.ErrorCode != SessionErrorCode.RewardedAdRejected) {
                await DisposeAsync(ex);
            }

            throw;
        }
        catch (UnauthorizedAccessException ex) {
            await DisposeAsync(ex);
            throw;
        }
        catch (Exception ex) {
            if (_disposed)
                throw;

            var now = FastDateTime.Now;
            _lastConnectionErrorTime ??= now;

            // dispose by session timeout and must before pause because SessionTimeout is bigger than ReconnectTimeout
            if (now - _lastConnectionErrorTime.Value > Config.SessionTimeout)
                await DisposeAsync(ex);

            // pause after retry limit
            else if (now - _lastConnectionErrorTime.Value > Config.UnstableTimeout) {
                _autoWaitTime = now;
                if (_sessionStatus != null) _sessionStatus.WaitingCount++;
                State = ClientState.Waiting;
                VhLogger.Instance.LogWarning(ex, "Client is paused because of too many connection errors.");
            }

            // set unstable state if it could not establish any connection
            else if (State == ClientState.Connected) {
                if (_sessionStatus != null) _sessionStatus.UnstableCount++;
                State = ClientState.Unstable; //unstable
            }

            throw;
        }
    }

    public async Task UpdateSessionStatus(CancellationToken cancellationToken)
    {
        // don't use SendRequest because it can be disposed
        using var requestResult = await SendRequest<SessionResponse>(
                new SessionStatusRequest {
                    RequestId = UniqueIdFactory.Create(),
                    SessionId = SessionId,
                    SessionKey = SessionKey
                },
                cancellationToken)
            .Vhc();
    }

    public void SetWaitForAd()
    {
        _isPassthroughForAd = true;
        FireStateChanged();
    }

    public Task SetAdFailed(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        // if there is no wait for ad, then we should remove passthrough flag and resume the connection
        // App is responsible to disconnect for failed ad
        if (_waitForAdCts == null)
            _isPassthroughForAd = false;

        // first step should always be accepted to jump to the next step
        _waitForAdCts?.TrySetResult();
        FireStateChanged();
        return Task.CompletedTask;
    }

    public Task SetAdOk(CancellationToken cancellationToken)
    {
        // make everything is ok. 
        _ = cancellationToken;
        _isPassthroughForAd = false;
        _clientHost.DropCurrentConnections();
        _waitForAdCts?.TrySetResult();
        FireStateChanged();
        return Task.CompletedTask;
    }

    public async Task SetRewardedAdOk(string adData, CancellationToken cancellationToken)
    {
        // request reward from server
        using var requestResult = await SendRequest<SessionResponse>(
            new RewardedAdRequest {
                RequestId = UniqueIdFactory.Create(),
                SessionId = SessionId,
                SessionKey = SessionKey,
                AdData = adData
            },
            cancellationToken).Vhc();

        await SetAdOk(cancellationToken);
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        if (FastDateTime.UtcNow > _sessionStatus?.SessionExpirationTime) {
            var ex = new SessionException(SessionErrorCode.SessionExpired);
            VhLogger.Instance.LogError(GeneralEventId.Session, ex, "Session has been expired.");
            return DisposeAsync(ex);
        }

        return default;
    }

    private ValueTask DisposeAsync(Exception ex)
    {
        // DisposeAsync will try SendByte, and it may cause calling this dispose method again and go to deadlock
        if (_disposed || _disposeLock.IsLocked) // IsLocked means that DisposeAsync is already running
            return ValueTask.CompletedTask;

        VhLogger.Instance.LogDebug(ex, "Client is disposing due an error.");
        LastException = ex;
        return DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        using var lockScope = await _disposeLock.LockAsync();
        if (_disposed)
            return;

        // set state to disconnecting
        VhLogger.Instance.LogInformation("Client is getting ready to shutdown...");

        // save the state before disposal
        var shouldSendBye = LastException == null && State == ClientState.Connected;

        // stop adapter and events before sending bye request
        DisposeInternal();

        // dispose async resources
        var byeTimeout = TunnelDefaults.ByeTimeout.WhenNoDebugger();

        // close tracker
        if (_clientUsageTracker != null) {
            using var cts = new CancellationTokenSource(byeTimeout);
            var cancellationToken = cts.Token;
            await VhUtils.TryInvokeAsync(null, () => _clientUsageTracker.Report(cancellationToken)).Vhc();
        }

        // Sending Bye if the session was active before disposal
        if (shouldSendBye) {
            VhLogger.Instance.LogInformation("Sending bye to the server...");
            try {
                // don't use SendRequest because it can be disposed
                using var byteCts = new CancellationTokenSource(byeTimeout);
                using var requestResult = await ConnectorService.SendRequest<SessionResponse>(
                        new ByeRequest {
                            RequestId = UniqueIdFactory.Create(),
                            SessionId = SessionId,
                            SessionKey = SessionKey
                        },
                        byteCts.Token)
                    .Vhc();

                requestResult.ClientStream.DisposeWithoutReuse();
                VhLogger.Instance.LogInformation("Session has been closed on the server successfully.");
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(GeneralEventId.Request, ex, "Could not send the bye to the server..");
            }
        }

        Dispose();
    }

    private void DisposeInternal()
    {
        lock (_disposeLock) {
            if (_disposedInternal) return;
            _disposedInternal = true;
        }

        // shutdown
        VhLogger.Instance.LogInformation("Client is shutting down...");
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();

        _cleanupJob.Dispose();
        State = ClientState.Disconnecting;
        _waitForAdCts?.TrySetCanceled();
        _isPassthroughForAd = false;

        // stop reusing tcp connections for faster disposal
        if (_connectorService != null)
            _connectorService.AllowTcpReuse = false;

        // stop processing tunnel & adapter packets
        _vpnAdapter.PacketReceived -= VpnAdapter_PacketReceived;
        _tunnel.PacketReceived -= Tunnel_PacketReceived;
        _clientHost.PacketReceived -= ClientHost_PacketReceived;
        _proxyManager.PacketReceived -= Proxy_PacketReceived;

        VhLogger.Instance.LogDebug("Disposing ClientHost...");
        _clientHost.Dispose();

        // Tunnel
        VhLogger.Instance.LogDebug("Disposing Tunnel...");
        _tunnel.Dispose();

        VhLogger.Instance.LogDebug("Disposing ProxyManager...");
        _proxyManager.Dispose();

        // Make sure async resources are disposed
        _clientUsageTracker?.Dispose();

        // disposing VpnAdapter. It must be at the end of the disposal process so channels can be disposed properly
        // because network change events can cause problems
        if (Config.AutoDisposeVpnAdapter) {
            VhLogger.Instance.LogDebug("Stopping the VpnAdapter...");
            if (_vpnAdapter.IsStarted)
                VhUtils.TryInvoke("Stop the VpnAdapter", () => _vpnAdapter.Stop());
        }
    }

    public void Dispose()
    {
        lock (_disposeLock) {
            if (_disposed) return;
            _disposed = true;
        }

        // dispose all resources before bye request
        DisposeInternal();

        // dispose ConnectorService before ProxyEndPointManager as it uses ProxyEndPointManager
        VhLogger.Instance.LogDebug("Disposing ConnectorService...");
        _connectorService?.Dispose();

        // dispose ProxyEndPointManager before adapter get closed and it needs Adapter's SocketFactory
        VhLogger.Instance.LogDebug("Disposing ProxyEndPointManager...");
        ProxyEndPointManager.Dispose();

        // disposing adapter
        VhLogger.Instance.LogDebug("Disposing Adapter...");
        _vpnAdapter.Dispose();

        State = ClientState.Disposed; //everything is clean

        // Changing state fire events in a task, so we should not do it immediately after disposing
        // StateChanged = null; 
        VhLogger.Instance.LogInformation("Bye Bye!");
    }

    private class ClientSessionStatus(VpnHoodClient client, AccessUsage accessUsage) : ISessionStatus
    {
        private AccessUsage _accessUsage = accessUsage;
        internal void Update(AccessUsage? value) => _accessUsage = value ?? _accessUsage;

        public ClientConnectorStatus ConnectorStatus => client.ConnectorService.Status;
        public Traffic Speed => client._tunnel.Speed;
        public Traffic SessionTraffic => client._tunnel.Traffic;
        public Traffic SessionSplitTraffic => client._proxyManager.Traffic;

        public Traffic CycleTraffic {
            get => field + client._tunnel.Traffic;
        } = accessUsage.CycleTraffic;

        public Traffic TotalTraffic {
            get => field + client._tunnel.Traffic;
        } = accessUsage.TotalTraffic;

        public int SessionPacketChannelCount => client._sessionPacketChannelCount;
        public int TcpTunnelledCount => client._clientHost.Stat.TcpTunnelledCount;
        public int TcpPassthruCount => client._clientHost.Stat.TcpPassthruCount;
        public int ActivePacketChannelCount => client._tunnel.PacketChannelCount;
        public bool IsDropQuic => client.IsDropQuic;
        public bool IsTcpProxy => client.IsTcpProxy;
        public ChannelProtocol ChannelProtocol => client.ChannelProtocol;
        public int UnstableCount { get; set; }
        public int WaitingCount { get; set; }
        public bool CanExtendByRewardedAd => _accessUsage.CanExtendByRewardedAd;
        public int UserReviewRecommended => _accessUsage.UserReviewRecommended;
        public long SessionMaxTraffic => _accessUsage.MaxTraffic;
        public DateTime? SessionExpirationTime => _accessUsage.ExpirationTime;
        public int? ActiveClientCount => _accessUsage.ActiveClientCount;
        public bool IsDnsOverTlsDetected => client._isDnsOverTlsDetected;
    }
}
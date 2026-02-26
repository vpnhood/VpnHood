using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

internal class ClientSession : IDisposable, IAsyncDisposable
{
    private readonly ISocketFactory _socketFactory;
    private readonly IVpnAdapter _vpnAdapter;
    private readonly NetFilter _netFilter;
    private readonly Tunnel _tunnel;
    private readonly ClientUsageTracker? _clientUsageTracker;
    private readonly ProxyManager _proxyManager;
    private readonly ClientPacketHandler _packetHandler;
    private readonly ClientHost _clientHost;
    private readonly ConnectorService _connectorService;
    private readonly ClientSessionStatus _status;
    private readonly Job _cleanupJob;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly AsyncLock _disposeLock = new();
    private readonly AsyncLock _packetChannelLock = new();
    private DateTime? _autoWaitTime;
    private ClientUdpChannelTransmitter? _udpTransmitter;

    private bool _disposed;
    private ChannelProtocol _channelProtocol;
    private ChannelProtocol _oldChannelProtocol;
    private bool _useTcpProxy;
    private bool _dropQuic;
    private bool _dropUdp;
    private DateTime? _lastConnectionErrorTime;

    public event EventHandler? StateChanged;
    public ISessionStatus Status => _status;
    public ulong SessionId { get; }
    public byte[] SessionKey { get; }
    public SessionInfo SessionInfo { get; }
    public ClientSessionConfig Config { get; }
    public ISessionAdHandler AdHandler { get; }
    public Exception? LastException { get; private set; }
    public int CreatedPacketChannelCount { get; private set; }
    public bool IsAdapterStarted => _vpnAdapter.IsStarted;
    public bool PassthroughForAd {
        get => _packetHandler.PassthroughForAd;
        set => _packetHandler.PassthroughForAd = value;
    }

    public ClientSession(
        IVpnAdapter vpnAdapter,
        ISocketFactory socketFactory,
        ITracker? tracker,
        SessionInfo sessionInfo,
        ulong sessionId,
        byte[] sessionKey,
        AccessUsage accessUsage,
        ConnectorService connectorService,
        DomainFilteringService domainFilteringService,
        NetFilter netFilter,
        ClientSessionOptions options,
        ClientSessionConfig config)
    {
        _netFilter = netFilter;
        _connectorService = connectorService;
        _channelProtocol = options.ChannelProtocol;
        _oldChannelProtocol = options.ChannelProtocol;
        _useTcpProxy = options.UseTcpProxy;
        _dropQuic = options.DropQuic;
        _dropUdp = options.DropUdp;
        _socketFactory = socketFactory;
        Config = config;
        SessionInfo = sessionInfo;
        SessionKey = sessionKey;
        SessionId = sessionId;

        // init VPN adapter
        _vpnAdapter = vpnAdapter;
        vpnAdapter.PrimaryAdapterIpChanged += VpnAdapter_PrimaryAdapterIpChanged;
        vpnAdapter.PacketReceived += VpnAdapter_PacketReceived;

        // Tunnel
        _tunnel = new Tunnel(new TunnelOptions {
            AutoDisposePackets = true,
            PacketQueueCapacity = TunnelDefaults.TunnelPacketQueueCapacity,
            MaxPacketChannelCount = _channelProtocol == ChannelProtocol.Udp ? 1 : Config.MaxPacketChannelCount,
            UseSpeedometerTimer = true
        });
        _tunnel.RemoteMtu = Config.RemoteMtu;
        _tunnel.PacketReceived += Tunnel_PacketReceived;

        // delegator
        _proxyManager = new ProxyManager(socketFactory, new ProxyManagerOptions {
            IsPingSupported = false,
            PacketProxyCallbacks = null,
            UdpTimeout = TunnelDefaults.UdpTimeout,
            MaxUdpClientCount = TunnelDefaults.MaxUdpClientCount,
            MaxPingClientCount = TunnelDefaults.MaxPingClientCount,
            PacketQueueCapacity = TunnelDefaults.ProxyPacketQueueCapacity,
            IcmpTimeout = TunnelDefaults.IcmpTimeout,
            UdpBufferSize = Config.UdpProxyBufferSize ?? TunnelDefaults.ClientUdpProxyBufferSize,
            LogScope = null,
            UseUdpProxy2 = true,
            AutoDisposePackets = true
        });
        _proxyManager.PacketReceived += Proxy_PacketReceived;

        // Stream handler
        var streamHandler = new ClientStreamHandler(
            this,
            sessionId: sessionId,
            sessionKey: sessionKey,
            domainFilterService: domainFilteringService,
            socketFactory: socketFactory,
            tunnel: _tunnel,
            tcpConnectTimeout: Config.TcpConnectTimeout,
            proxyManager: _proxyManager,
            netFilter: _netFilter,
            streamProxyBufferSize: Config.StreamProxyBufferSize);

        // proxy host
        _clientHost = new ClientHost(
            streamHandler,
            catcherAddressIpV4: options.TcpProxyCatcherAddressIpV4,
            catcherAddressIpV6: options.TcpProxyCatcherAddressIpV6);
        _clientHost.PacketReceived += ClientHost_PacketReceived;

        // packet handler
        _packetHandler = new ClientPacketHandler(
            tunnel: _tunnel,
            clientHost: _clientHost,
            domainFilteringService: domainFilteringService,
            netFilter: _netFilter,
            proxyManager: _proxyManager,
            dnsServers: Config.DnsConfig.DnsServers,
            isIpV6SupportedByServer: Config.IsIpV6SupportedByServer);

        _status = new ClientSessionStatus(
            session: this,
            tunnel: _tunnel,
            connectorService: connectorService,
            proxyManager: _proxyManager,
            streamHandler: streamHandler,
            packetHandler: _packetHandler,
            accessUsage: accessUsage);

        if (tracker != null)
            _clientUsageTracker = new ClientUsageTracker(_status, tracker);

        // Ad
        AdHandler = new SessionAdHandler(this);
        AdHandler.IsWaitingForChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);

        // Create simple disposable objects
        _cancellationTokenSource = new CancellationTokenSource();
        _cleanupJob = new Job(Cleanup, nameof(VpnHoodClient));

        // apply config
        UpdateConfig();

        // start
        _clientHost.Start();
    }

    private bool ShouldManagePacketChannels {
        get => _tunnel.PacketChannelCount < _tunnel.MaxPacketChannelCount;
    }

    public ClientState State {
        get {
            if (field is ClientState.Disposed or ClientState.Disconnecting) return field;
            if (AdHandler.IsWaitingForAd) return ClientState.WaitingForAd;
            return field;
        }
        private set {
            if (field == value) return;
            field = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    } = ClientState.Connected;

    public bool DropUdp {
        get => _dropUdp;
        set {
            if (_dropUdp == value) return;
            _dropUdp = value;
            UpdateConfig();
        }
    }

    public bool DropQuic {
        get => _dropQuic;
        set {
            if (_dropQuic == value) return;
            _dropQuic = value;
            UpdateConfig();
        }
    }

    public bool UseTcpProxy {
        get => _useTcpProxy;
        set {
            if (_useTcpProxy == value) return;
            _useTcpProxy = value;
            UpdateConfig();
        }
    }

    public ChannelProtocol ChannelProtocol {
        get => _channelProtocol;
        set {
            if (_channelProtocol == value) return;
            _channelProtocol = value;
            UpdateConfig();
        }
    }

    private bool CalcUseTcpProxy()
    {
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
        return _useTcpProxy;
    }

    private void UpdateConfig()
    {
        // UseTcp
        var useTcpProxy = CalcUseTcpProxy();

        // DropQuic is useless if we don't use tcp proxy
        var dropQuic = _dropQuic && CalcUseTcpProxy();

        // DropUdp is useless if we don't use tcp proxy
        var dropUdp = _dropUdp && CalcUseTcpProxy();

        // ChannelProtocol
        var channelProtocol = ChannelProtocolValidator.Validate(_channelProtocol, SessionInfo);
        if (channelProtocol != _oldChannelProtocol) {
            VhLogger.Instance.LogInformation("VpnProtocol is changed to {VpnProtocol}.", channelProtocol);
            _tunnel.MaxPacketChannelCount = channelProtocol == ChannelProtocol.Udp ? 1 : Config.MaxPacketChannelCount;
            _channelProtocol = channelProtocol;
            _oldChannelProtocol = channelProtocol;
            _tunnel.RemoveChannels<IPacketChannel>();
            Task.Run(() => ManagePacketChannels(_cancellationTokenSource.Token));
        }

        if (useTcpProxy != _useTcpProxy)
            VhLogger.Instance.LogWarning("UseTcpProxy is changed to {UseTcpProxy} because of config or capability change.", _packetHandler.UseTcpProxy);

        if (dropQuic != _dropQuic)
            VhLogger.Instance.LogWarning("DropQuic is changed to {DropQuic} because client can not use TcpProxy.", _packetHandler.DropQuic);

        if (dropUdp != _dropUdp)
            VhLogger.Instance.LogWarning("DropUdp is changed to {DropUdp} because client can not use TcpProxy.", dropUdp);

        // update handlers
        _packetHandler.UseTcpProxy = useTcpProxy;
        _packetHandler.DropQuic = dropQuic;
        _packetHandler.DropUdp = dropUdp;
        _dropQuic = dropQuic;
        _dropUdp = dropUdp;

        // update ipv6 support
        var isIpV6Supported = _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6);
        _packetHandler.IsIpV6SupportedByClient = isIpV6Supported;
        _proxyManager.IsIpV6Supported = isIpV6Supported;
    }

    private void VpnAdapter_PrimaryAdapterIpChanged(object? sender, EventArgs e)
    {
        UpdateConfig();
    }

    private void VpnAdapter_PacketReceived(object? sender, IpPacket ipPacket)
    {
        // stop traffic if the client has been disposed
        if (_disposed)
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

        // simple process if domain filtering is not enabled to improve performance
        _packetHandler.ProcessOutgoingPacket(ipPacket);
    }

    private void ClientHost_PacketReceived(object? sender, IpPacket ipPacket) =>
        ProcessIncomingPacket(ipPacket, true);

    private void Proxy_PacketReceived(object? sender, IpPacket ipPacket) =>
        ProcessIncomingPacket(ipPacket, true);

    private void Tunnel_PacketReceived(object? sender, IpPacket ipPacket) =>
        ProcessIncomingPacket(ipPacket, false);

    // WARNING: Performance Critical!
    private void ProcessIncomingPacket(IpPacket ipPacket, bool useMapper)
    {
        if (useMapper && _netFilter.IpMapper?.FromHost(ipPacket.Protocol, ipPacket.GetSourceEndPoint(), out var newEndPoint) == true) {
            ipPacket.SetSourceEndPoint(newEndPoint);
            ipPacket.UpdateAllChecksums();
        }

        _vpnAdapter.SendPacketQueued(ipPacket);
    }

    public async ValueTask ManagePacketChannels(CancellationToken cancellationToken)
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
            if (_channelProtocol == ChannelProtocol.Udp)
                AddUdpChannel();
            else
                await AddTcpPacketChannel(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            if (!_disposed)
                VhLogger.LogError(GeneralEventId.PacketChannel, ex, "Could not Manage PacketChannels.");
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
        try {
            // find timespan
            var lifespan = VhUtils.IsInfinite(Config.MaxPacketChannelLifespan)
                ? (TimeSpan?)null
                : TimeSpan.FromSeconds(new Random().Next((int)Config.MinPacketChannelLifespan.TotalSeconds,
                    (int)Config.MaxPacketChannelLifespan.TotalSeconds));

            // PacketChannel should not be reused, otherwise its timespan will be meaningless
            if (lifespan != null) {
                requestResult.Connection.PreventReuse();
            }

            // add the new channel
            var channel = new StreamPacketChannel(new StreamPacketChannelOptions {
                Connection = requestResult.Connection,
                BufferSize = TunnelDefaults.ConnectionPacketBufferSize,
                ChannelId = request.RequestId,
                Blocking = true,
                AutoDisposePackets = true,
                Lifespan = lifespan
            });
            _tunnel.AddChannel(channel, true);
            CreatedPacketChannelCount++;
        }
        catch {
            requestResult.Dispose();
            throw;
        }
    }

    private void AddUdpChannel()
    {
        if (VhUtils.IsNullOrEmpty(SessionKey)) throw new Exception("Server UdpKey has not been set.");
        if (Config.HostUdpEndPoint == null) throw new Exception("Server does not serve any UDP endpoint.");

        // create channelTransmitter if not created
        _udpTransmitter ??= new ClientUdpChannelTransmitter(
            socketFactory: _socketFactory,
            sessionId: SessionId,
            sessionKey: SessionKey,
            remoteEndPoint: Config.HostUdpEndPoint,
            bufferSize: TunnelDefaults.ClientUdpChannelBufferSize);

        // create udp channel
        var udpChannel = new UdpChannel(_udpTransmitter.UdpTransport, new UdpChannelOptions {
            AutoDisposePackets = true,
            LeaveUdpTransportOpen = true,
            Blocking = true,
            ChannelId = Guid.NewGuid().ToString(),
            Lifespan = null
        });

        // add to tunnel
        try {
            _tunnel.AddChannel(udpChannel);
        }
        catch {
            udpChannel.Dispose();
            throw;
        }
    }

    internal Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequest request,
        CancellationToken cancellationToken) where T : SessionResponse
    {
        return SendRequest<T>(new ClientRequestEx { Request = request }, cancellationToken);
    }

    internal async Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequestEx request,
        CancellationToken cancellationToken) where T : SessionResponse
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try {
            // create a connection and send the request 
            var requestResult = await _connectorService.SendRequest<T>(request, cancellationToken).Vhc();
            _status.Update(requestResult.Response.AccessUsage);

            // client is disposed meanwhile
            if (_disposed) {
                requestResult.Dispose();
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            _lastConnectionErrorTime = null;
            State = ClientState.Connected; // stable state

            return requestResult;
        }
        catch (SessionException ex) {
            _status.Update(ex.SessionResponse.AccessUsage);

            // SessionException means that the request accepted by server but there is an error for that request
            _lastConnectionErrorTime = null;

            // close session if server has ended the session
            if (ex.SessionResponse.ErrorCode != SessionErrorCode.GeneralError &&
                ex.SessionResponse.ErrorCode != SessionErrorCode.RewardedAdRejected) {
                await DisposeAsync(ex);
            }

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
                _status.WaitingCount++;
                State = ClientState.Waiting;
                VhLogger.Instance.LogWarning(ex, "Client is paused because of too many connection errors.");
            }

            // set unstable state if it could not establish any connection
            else if (State == ClientState.Connected) {
                _status.UnstableCount++;
                State = ClientState.Unstable; //unstable
            }

            throw;
        }
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        if (FastDateTime.UtcNow > _status.SessionExpirationTime) {
            var ex = new SessionException(SessionErrorCode.SessionExpired);
            VhLogger.Instance.LogError(GeneralEventId.Session, ex, "Session has been expired.");
            return DisposeAsync(ex);
        }

        return default;
    }

    public void DropCurrentConnections()
    {
        _clientHost.DropCurrentConnections();
        _tunnel.RemoveChannels<IProxyChannel>();
    }

    public async Task UpdateStatus(CancellationToken cancellationToken)
    {
        // all request will update the status
        using var requestResult = await SendRequest<SessionResponse>(
                new SessionStatusRequest {
                    RequestId = UniqueIdFactory.Create(),
                    SessionId = SessionId,
                    SessionKey = SessionKey
                },
                cancellationToken)
            .Vhc();
    }



    private ValueTask DisposeAsync(Exception ex)
    {
        // DisposeAsync will try SendByte, and it may cause calling this dispose method again and go to deadlock
        if (_disposed || _disposeLock.IsLocked) // IsLocked means that DisposeAsync is already running
            return ValueTask.CompletedTask;

        VhLogger.Instance.LogDebug(ex, "Session is closing due to an error.");
        LastException = ex;
        return DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        using var lockScope = await _disposeLock.LockAsync();
        if (_disposed)
            return;

        // set state to disconnecting
        VhLogger.Instance.LogInformation("Session is closing...");
        State = ClientState.Disconnecting;

        // stop adapter and events before sending bye request to free client from VPN as fast as possible and 
        // Do not dispose VpnAdapter. It must be at the end of the disposal process so channels can be disposed properly
        // network change events can cause problems too
        if (_vpnAdapter.IsStarted)
            VhUtils.TryInvoke("Stop the VpnAdapter", () => _vpnAdapter.Stop());

        // dispose async resources

        // Sending Bye if the session was active before disposal
        var shouldSendBye = LastException == null;
        if (shouldSendBye) {
            VhLogger.Instance.LogInformation("Sending bye to the server...");
            try {
                // don't use SendRequest because it can be disposed
                var byeTimeout = TunnelDefaults.ByeTimeout.WhenNoDebugger();
                using var byteCts = new CancellationTokenSource(byeTimeout);
                using var requestResult = await _connectorService.SendRequest<SessionResponse>(
                        new ByeRequest {
                            RequestId = UniqueIdFactory.Create(),
                            SessionId = SessionId,
                            SessionKey = SessionKey
                        },
                        byteCts.Token)
                    .Vhc();

                requestResult.Connection.PreventReuse();
                requestResult.Connection.Dispose();
                VhLogger.Instance.LogInformation("Session has been closed on the server successfully.");
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(GeneralEventId.Request, ex, "Could not send the bye to the server..");
            }
        }

        await _cancellationTokenSource.TryCancelAsync();
        Dispose();
    }

    public void Dispose()
    {
        lock (_disposeLock) {
            if (_disposed) return;
            _disposed = true;
        }

        State = ClientState.Disconnecting;
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();

        // stop adapter and events before sending bye request
        // Do not dispose VpnAdapter. It must be at the end of the disposal process so channels can be disposed properly
        // network change events can cause problems too
        if (_vpnAdapter.IsStarted)
            VhUtils.TryInvoke("Stop the VpnAdapter", () => _vpnAdapter.Stop());

        // stop processing tunnel & adapter packets
        _vpnAdapter.PacketReceived -= VpnAdapter_PacketReceived;
        _vpnAdapter.PrimaryAdapterIpChanged -= VpnAdapter_PrimaryAdapterIpChanged;
        _tunnel.PacketReceived -= Tunnel_PacketReceived;
        _clientHost.PacketReceived -= ClientHost_PacketReceived;
        _proxyManager.PacketReceived -= Proxy_PacketReceived;

        _packetHandler.PassthroughForAd = false;

        // stop reusing tcp connections for faster disposal
        _connectorService.AllowTcpReuse = false;

        VhLogger.Instance.LogDebug("Disposing ClientHost...");
        _clientHost.Dispose();

        VhLogger.Instance.LogDebug("Disposing Tunnel...");
        _tunnel.Dispose();

        VhLogger.Instance.LogDebug("Disposing UdpTransmitter...");
        _udpTransmitter?.Dispose();

        VhLogger.Instance.LogDebug("Disposing ProxyManager...");
        _proxyManager.Dispose();

        _cleanupJob.Dispose();
        _clientUsageTracker?.Dispose();
        AdHandler.Dispose();

        // invoke disposed event
        State = ClientState.Disposed;
    }
}

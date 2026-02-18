using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net.Extensions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Server.Exceptions;
using VpnHood.Core.Server.Utils;
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

namespace VpnHood.Core.Server;

public class Session : IDisposable
{
    private readonly IAccessManager _accessManager;
    private readonly IVpnAdapter? _vpnAdapter;
    private readonly ISocketFactory _socketFactory;
    private readonly NetFilter _netFilter;
    private readonly ProxyManager _proxyManager;
    private readonly Lock _verifyRequestLock = new();
    private readonly int _maxTcpConnectWaitCount;
    private readonly int _maxTcpChannelCount;
    private readonly TransferBufferSize _streamProxyBufferSize;
    private readonly TransferBufferSize? _tcpKernelBufferSize;
    private readonly TrackingOptions _trackingOptions;
    private UdpChannel? _udpChannel;

    private readonly EventReporter _netScanExceptionReporter = new(
        "NetScan protector does not allow this request.", GeneralEventId.NetProtect);

    private readonly EventReporter _maxTcpChannelExceptionReporter = new(
        "Maximum TcpChannel has been reached.", GeneralEventId.NetProtect);

    private readonly EventReporter _maxTcpConnectWaitExceptionReporter = new(
        "Maximum TcpConnectWait has been reached.", GeneralEventId.NetProtect);

    private readonly EventReporter _filterReporter = new("Some requests have been blocked.",
        GeneralEventId.NetProtect);

    private Traffic _prevTraffic = new();
    private int _tcpConnectWaitCount;

    public Tunnel Tunnel { get; }
    public ulong SessionId { get; }
    public byte[] SessionKey { get; }
    public SessionResponse SessionResponseEx { get; internal set; }
    public bool IsDisposed => DisposedTime != null;
    public DateTime? DisposedTime { get; private set; }
    public NetScanDetector? NetScanDetector { get; }
    public SessionExtraData ExtraData { get; }
    public int ProtocolVersion { get; }
    public int TcpConnectWaitCount => _tcpConnectWaitCount;

    public int TcpChannelCount =>
        Tunnel.StreamProxyChannelCount + (_udpChannel != null ? 0 : Tunnel.PacketChannelCount);

    public int UdpConnectionCount => _proxyManager.UdpClientCount;
    public DateTime LastActivityTime => Tunnel.LastActivityTime;
    public VirtualIpBundle VirtualIps { get; }
    public bool AllowTcpPacket { get; }
    public bool AllowTcpProxy { get; }

    internal Session(IAccessManager accessManager,
        IVpnAdapter? vpnAdapter,
        ISocketFactory socketFactory,
        NetFilter netFilter,
        SessionResponseEx sessionResponseEx,
        SessionOptions options,
        TrackingOptions trackingOptions,
        SessionExtraData extraData,
        VirtualIpBundle virtualIps)
    {
        var sessionTuple = Tuple.Create("SessionId", (object?)sessionResponseEx.SessionId);
        var logScope = new LogScope();
        logScope.Data.Add(sessionTuple);

        _accessManager = accessManager;
        _vpnAdapter = vpnAdapter;
        _socketFactory = socketFactory;
        _proxyManager = new ProxyManager(socketFactory, new ProxyManagerOptions {
            UdpTimeout = options.UdpTimeoutValue,
            IcmpTimeout = options.IcmpTimeoutValue,
            MaxUdpClientCount = options.MaxUdpClientCountValue,
            MaxPingClientCount = options.MaxIcmpClientCountValue,
            UdpBufferSize = options.UdpProxyBufferSizeValue ?? TunnelDefaults.ServerUdpProxyBufferSize,
            LogScope = logScope,
            IsPingSupported = true,
            PacketProxyCallbacks = new PacketProxyCallbacks(this),
            AutoDisposePackets = true,
            PacketQueueCapacity = TunnelDefaults.ProxyPacketQueueCapacity,
            UseUdpProxy2 = options.UseUdpProxy2Value
        });
        _proxyManager.PacketReceived += Proxy_PacketsReceived;
        _trackingOptions = trackingOptions;
        _maxTcpConnectWaitCount = options.MaxTcpConnectWaitCountValue;
        _maxTcpChannelCount = options.MaxTcpChannelCountValue;
        _streamProxyBufferSize = options.StreamProxyBufferSize ?? TunnelDefaults.ServerStreamProxyBufferSize;
        _tcpKernelBufferSize = options.TcpKernelBufferSize;
        _netFilter = netFilter;
        _netScanExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpConnectWaitExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpChannelExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        AllowTcpPacket = options.AllowTcpPacketValue;
        AllowTcpProxy = options.AllowTcpProxyValue;
        ExtraData = extraData;
        VirtualIps = virtualIps;
        SessionResponseEx = sessionResponseEx;
        ProtocolVersion = sessionResponseEx.ProtocolVersion;
        SessionId = sessionResponseEx.SessionId;
        SessionKey = sessionResponseEx.SessionKey ?? throw new InvalidOperationException(
            $"{nameof(sessionResponseEx)} does not have {nameof(sessionResponseEx.SessionKey)}!");
        Tunnel = new Tunnel(new TunnelOptions {
            MaxPacketChannelCount = options.MaxPacketChannelCountValue,
            PacketQueueCapacity = TunnelDefaults.TunnelPacketQueueCapacity,
            AutoDisposePackets = true
        });
        Tunnel.PacketReceived += Tunnel_PacketReceived;

        // ReSharper disable once MergeIntoPattern
        if (options.NetScanLimit != null && options.NetScanTimeout != null)
            NetScanDetector = new NetScanDetector(options.NetScanLimit.Value, options.NetScanTimeout.Value);
    }


    public Traffic Traffic {
        get {
            // Intentionally Reversed: sending to tunnel means receiving form client,
            // Intentionally Reversed: receiving from tunnel means sending for client
            var traffic = Tunnel.Traffic - _prevTraffic;
            return new Traffic {
                Sent = traffic.Received,
                Received = traffic.Sent
            };
        }
    }

    public IUdpTransport UseUdpTransport(Func<IUdpTransport> factory)
    {
        // Note: creating UdpTransport is costly, this method may be called per packet
        _udpChannel ??= new UdpChannel(factory(), new UdpChannelOptions {
            Blocking = false,
            AutoDisposePackets = true,
            Lifespan = null,
            ChannelId = Guid.NewGuid().ToString()
        });

        UseUdpChannel = true;
        return _udpChannel.UdpTransport;
    }

    public Traffic ResetTraffic()
    {
        var traffic = Traffic;
        _prevTraffic = Tunnel.Traffic;
        return traffic;
    }

    public void SetSyncRequired() => IsSyncRequired = true;
    public bool IsSyncRequired { get; private set; }

    public bool ResetSyncRequired()
    {
        var oldValue = IsSyncRequired;
        IsSyncRequired = false;
        return oldValue;
    }

    private IPAddress GetClientVirtualIp(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? VirtualIps.IpV4 : VirtualIps.IpV6;
    }

    private void Proxy_PacketsReceived(object? sender, IpPacket ipPacket)
    {
        Proxy_PacketReceived(ipPacket);
    }

    public void Adapter_PacketReceived(object? sender, IpPacket ipPacket)
    {
        _ = sender;
        Proxy_PacketReceived(ipPacket);
    }

    private void Proxy_PacketReceived(IpPacket ipPacket)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        PacketLogger.LogPacket(ipPacket, "Delegating a packet to client...");
        if (_netFilter.IpMapper?.FromHost(ipPacket.Protocol, ipPacket.GetSourceEndPoint(), out var newEndPoint) == true) {
            ipPacket.SetSourceEndPoint(newEndPoint);
            ipPacket.UpdateAllChecksums();
        }

        // todo: must be asynchronous
        Tunnel.SendPacketQueued(ipPacket); // PacketEnqueue will dispose packets
    }

    private void Tunnel_PacketReceived(object? sender, IpPacket ipPacket)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // filter requests
        var virtualIp = GetClientVirtualIp(ipPacket.Version);

        // reject if packet source does not match client internal ip
        if (!ipPacket.SourceAddress.Equals(virtualIp)) {
            var ipeEndPointPair = ipPacket.GetEndPoints();
            LogTrack(ipPacket.Protocol, null, ipeEndPointPair.RemoteEndPoint.ToValue(), false, true, "NetFilter");
            _filterReporter.Raise();
            throw new NetFilterException(
                $"Invalid tunnel packet source ip. SourceIp: {VhLogger.Format(ipPacket.SourceAddress)}");
        }

        // check TcpPacket
        if (!AllowTcpPacket && ipPacket.Protocol == IpProtocol.Tcp)
            throw new NetFilterException("TcpPacket is not allowed in this session.");

        // filter before mapper because it supposes to filter user requests
        if (_netFilter.IpFilter?.Process(ipPacket.Protocol, ipPacket.GetDestinationEndPoint()) == FilterAction.Block) {
            LogTrack(ipPacket.Protocol, null, ipPacket.GetDestinationEndPoint(), false, true, "NetFilter");
            _filterReporter.Raise();
            throw new NetFilterException(
                $"Packet discarded due to the NetFilter's policies. DestinationIp: {VhLogger.Format(ipPacket.DestinationAddress)}");
        }

        // Map destination
        if (_netFilter.IpMapper?.ToHost(ipPacket.Protocol, ipPacket.GetDestinationEndPoint(), out var newEndPoint) == true) {
            ipPacket.SetDestinationEndPoint(newEndPoint);
            ipPacket.UpdateAllChecksums();
        }

        // send using tunnel or proxy
        if (_vpnAdapter?.IsIpVersionSupported(ipPacket.Version) == true)
            _vpnAdapter.SendPacketQueued(ipPacket);
        else
            _proxyManager.SendPacketQueued(ipPacket);
    }

    public void LogTrack(IpProtocol protocol, IpEndPointValue? localEndPoint, IpEndPointValue? destinationEndPoint,
        bool isNewLocal, bool isNewRemote, string? failReason)
    {
        if (!_trackingOptions.IsEnabled)
            return;

        if (_trackingOptions is { TrackDestinationIpValue: false, TrackDestinationPortValue: false } && !isNewLocal &&
            failReason == null)
            return;

        if (!_trackingOptions.TrackTcpValue && protocol is IpProtocol.Tcp ||
            !_trackingOptions.TrackUdpValue && protocol is IpProtocol.Udp ||
            !_trackingOptions.TrackUdpValue && protocol is IpProtocol.IcmpV4 or IpProtocol.IcmpV6)
            return;

        var mode = (isNewLocal ? "L" : "") + (isNewRemote ? "R" : "");
        var localPortStr = "-";
        var destinationIpStr = "-";
        var destinationPortStr = "-";
        var netScanCount = "-";
        failReason ??= "Ok";

        if (localEndPoint != null)
            localPortStr = _trackingOptions.TrackLocalPortValue ? localEndPoint.Value.Port.ToString() : "*";

        if (destinationEndPoint != null) {
            destinationIpStr = _trackingOptions.TrackDestinationIpValue
                ? VhUtils.RedactIpAddress(destinationEndPoint.Value.Address)
                : "*";
            destinationPortStr = _trackingOptions.TrackDestinationPortValue ? destinationEndPoint.Value.Port.ToString() : "*";
            netScanCount = NetScanDetector?.GetBurstCount(destinationEndPoint.Value).ToString() ?? "*";
        }

        VhLogger.Instance.LogInformation(GeneralEventId.Track,
            "{Proto,-4}\tSessionId {SessionId}\t{Mode,-2}\tTcpCount {TcpCount,4}\tUdpCount {UdpCount,4}\tTcpWait {TcpConnectWaitCount,3}\tNetScan {NetScan,3}\t" +
            "SrcPort {SrcPort,-5}\tDstIp {DstIp,-15}\tDstPort {DstPort,-5}\t{Success,-10}",
            protocol, SessionId, mode,
            TcpChannelCount, _proxyManager.UdpClientCount, _tcpConnectWaitCount, netScanCount,
            localPortStr, destinationIpStr, destinationPortStr, failReason);
    }

    public async Task ProcessTcpPacketChannelRequest(TcpPacketChannelRequest request, IConnection connection,
        CancellationToken cancellationToken)
    {
        // manage wait count
        Interlocked.Increment(ref _tcpConnectWaitCount);
        using var autoDispose = new AutoDispose(() => Interlocked.Decrement(ref _tcpConnectWaitCount));

        // send OK reply
        await connection.WriteResponseAsync(SessionResponseEx, cancellationToken).Vhc();

        // add channel
        VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
            "Creating a TcpPacketChannel channel. SessionId: {SessionId}", VhLogger.FormatSessionId(SessionId));

        var channel = new StreamPacketChannel(new StreamPacketChannelOptions {
            BufferSize = TunnelDefaults.ServerStreamPacketBufferSize,
            Blocking = false,
            AutoDisposePackets = true,
            Connection = connection,
            ChannelId = request.RequestId,
            Lifespan = null
        });

        Tunnel.AddChannel(channel, disposeIfFailed: true);
        UseUdpChannel = false;
    }

    public bool UseUdpChannel {
        get => _udpChannel != null && field;
        set {
            if (value == field)
                return;

            if (value) {
                if (_udpChannel is null)
                    throw new InvalidOperationException("UdpChannel is not created yet.");

                // enable udp channel
                Tunnel.RemoveAllPacketChannels();
                Tunnel.AddChannel(_udpChannel);
            }
            else {
                // disable udp channel
                Tunnel.RemoveAllChannels<UdpChannel>();
            }

            field = value;
        }
    }

    internal Task ProcessUdpPacketRequest(UdpPacketRequest request, IConnection connection,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = connection;
        _ = cancellationToken;
        throw new NotImplementedException();
    }

    internal async Task ProcessSessionStatusRequest(SessionStatusRequest request, IConnection connection,
        CancellationToken cancellationToken)
    {
        _ = request;
        await connection.DisposeAsync(SessionResponseEx, cancellationToken).Vhc();
    }

    internal async Task ProcessRewardedAdRequest(RewardedAdRequest request, IConnection connection,
        CancellationToken cancellationToken)
    {
        SessionResponseEx = await _accessManager
            .Session_AddUsage(sessionId: SessionId, new Traffic(), adData: request.AdData, cancellationToken).Vhc();
        await connection.DisposeAsync(SessionResponseEx, cancellationToken).Vhc();
    }

    internal async Task ProcessTcpProxyRequest(StreamProxyChannelRequest request, IConnection connection,
        CancellationToken cancellationToken)
    {
        if (!AllowTcpProxy)
            throw new SessionException(SessionErrorCode.GeneralError, "TcpProxy is not allowed in this session.");

        TcpClient? tcpClientHost = null;
        IConnection? tcpConnectionHost = null;
        try {
            // manage wait count
            Interlocked.Increment(ref _tcpConnectWaitCount);

            // filter before mapper because it supposes to filter user requests
            if (_netFilter.IpFilter?.Process(IpProtocol.Tcp, request.DestinationEndPoint.ToValue()) == FilterAction.Block) {
                LogTrack(IpProtocol.Tcp, null, request.DestinationEndPoint.ToValue(), false, true, "NetFilter");
                _filterReporter.Raise();
                throw new NetFilterException(
                    $"Packet discarded due to the NetFilter's policies. DestinationIp: {VhLogger.Format(request.DestinationEndPoint)}");
            }

            // IpMapper
            if (_netFilter.IpMapper?.ToHost(IpProtocol.Tcp, request.DestinationEndPoint.ToValue(), out var newEndPoint) == true) {
                request.DestinationEndPoint = newEndPoint.ToIPEndPoint();
            }

            // log with new destination
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
                "Connecting to the requested endpoint. RequestedEP: {Format}",
                VhLogger.Format(request.DestinationEndPoint));

            // Apply limitation before create connection to host
            VerifyTcpChannelRequest(connection, request);

            //set reuseAddress to  true to prevent error only one usage of each socket address is normally permitted
            tcpClientHost = _socketFactory.CreateTcpClient(request.DestinationEndPoint);
            VhUtils.ConfigTcpClient(tcpClientHost,
                sendBufferSize: _tcpKernelBufferSize?.Send,
                receiveBufferSize: _tcpKernelBufferSize?.Receive);

            // connect to requested destination
            try {
                await tcpClientHost.ConnectAsync(request.DestinationEndPoint, cancellationToken).Vhc();
                tcpConnectionHost = new TcpConnection(tcpClientHost, connectionId: request.RequestId,
                    connectionName: "host", isServer: true);
            }
            catch (Exception ex) {
                var message =
                    $"{ex.Message} RequestEndPoint: {VhLogger.Format(request.DestinationEndPoint)}, RequestId: {request.RequestId}";
                throw new SessionException(SessionErrorCode.GeneralError, message);
            }

            //tracking
            LogTrack(IpProtocol.Tcp,
                localEndPoint: tcpClientHost.TryGetLocalEndPoint()?.ToValue(),
                destinationEndPoint: request.DestinationEndPoint.ToValue(),
                isNewLocal: true, isNewRemote: true, failReason: null);

            // send response, using original cancellation token without timeout
            // ReSharper disable once PossiblyMistakenUseOfCancellationToken
            await connection.WriteResponseAsync(SessionResponseEx, cancellationToken).Vhc();

            // add the connection
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "Adding a ProxyChannel.");
            var proxyChannel = new ProxyChannel(connection.ToString()!, tcpConnectionHost, connection,
                _streamProxyBufferSize);

            Tunnel.AddChannel(proxyChannel, disposeIfFailed: true);
        }
        catch {
            tcpClientHost?.Dispose();
            tcpConnectionHost?.Dispose();
            throw;
        }
        finally {
            Interlocked.Decrement(ref _tcpConnectWaitCount);
        }
    }

    private void VerifyTcpChannelRequest(IConnection connection, StreamProxyChannelRequest request)
    {
        lock (_verifyRequestLock) {
            // NetScan limit
            VerifyNetScan(IpProtocol.Tcp, request.DestinationEndPoint.ToValue(), request.RequestId);

            // Channel Count limit
            if (TcpChannelCount + _tcpConnectWaitCount > _maxTcpChannelCount) {
                LogTrack(IpProtocol.Tcp, null, request.DestinationEndPoint.ToValue(), false, true, "MaxTcp");
                _maxTcpChannelExceptionReporter.Raise();
                throw new MaxTcpChannelException(connection.RemoteEndPoint, this, request.RequestId);
            }

            // Check tcp wait limit
            if (TcpConnectWaitCount > _maxTcpConnectWaitCount) {
                LogTrack(IpProtocol.Tcp, null, request.DestinationEndPoint.ToValue(), false, true, "MaxTcpWait");
                _maxTcpConnectWaitExceptionReporter.Raise();
                throw new MaxTcpConnectWaitException(connection.RemoteEndPoint, this,
                    request.RequestId);
            }
        }
    }

    private void VerifyNetScan(IpProtocol protocol, IpEndPointValue remoteEndPoint, string requestId)
    {
        if (NetScanDetector == null || NetScanDetector.Verify(remoteEndPoint)) return;

        LogTrack(protocol, null, remoteEndPoint, false, true, "NetScan");
        _netScanExceptionReporter.Raise();
        throw new NetScanException(remoteEndPoint.ToIPEndPoint(), this, requestId);
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        _proxyManager.PacketReceived -= Proxy_PacketsReceived;
        _proxyManager.Dispose();
        Tunnel.PacketReceived -= Tunnel_PacketReceived;
        Tunnel.Dispose();
        _netScanExceptionReporter.Dispose();
        _maxTcpChannelExceptionReporter.Dispose();
        _maxTcpConnectWaitExceptionReporter.Dispose();
        _filterReporter.Dispose();
        NetScanDetector?.Dispose();

        // if there is no reason it is temporary
        var reason = "Cleanup";
        if (SessionResponseEx.ErrorCode != SessionErrorCode.Ok)
            reason = SessionResponseEx.ErrorCode == SessionErrorCode.SessionClosed ? "User" : "Access";

        // Report removing session
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tActor: {Actor,-7}\tSuppressBy: {SuppressedBy,-8}\tErrorCode: {ErrorCode,-20}\tMessage: {message}",
            SessionId, "Close", reason, SessionResponseEx.SuppressedBy, SessionResponseEx.ErrorCode,
            SessionResponseEx.ErrorMessage ?? "None");

        // it must be ended to let manager know that session is disposed and finish all tasks
        DisposedTime = DateTime.UtcNow;
        SetSyncRequired();
    }

    private class PacketProxyCallbacks(Session session) : IPacketProxyCallbacks
    {
        public void OnConnectionRequested(IpProtocol protocolType, IpEndPointValue remoteEndPoint)
        {
            session.VerifyNetScan(protocolType, remoteEndPoint, "OnNewRemoteEndPoint");
        }

        public void OnConnectionEstablished(IpProtocol protocolType, IpEndPointValue localEndPoint,
            IpEndPointValue remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
            session.LogTrack(protocolType, localEndPoint, remoteEndPoint, isNewLocalEndPoint,
                isNewRemoteEndPoint, null);
        }
    }
}
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Server.Exceptions;
using VpnHood.Core.Server.Utils;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Server;

public class Session : IDisposable
{
    private const double ReceiveSpeedGrace = 1.2;
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

    private readonly EventReporter _filterBlockDestinationReporter = new("Some destinations have been blocked.", GeneralEventId.NetProtect);
    private readonly EventReporter _filterBadSourceReporter = new("Some packets with invalid sources have been blocked.", GeneralEventId.NetProtect);

    private Traffic _prevTraffic = new();
    private long _responseRequestTimestamp; // when the request whose reply is Response went out
    private int _tcpConnectWaitCount;
    private int _netScanErrorCount;
    private readonly Lock _stateLock = new();

    public Tunnel Tunnel { get; }
    public ulong SessionId { get; }
    public byte[] SessionKey { get; }
    public SessionResponse Response { get; private set; }
    public bool IsDisposed => DisposedTime != null;
    public DateTime? DisposedTime { get; private set; }
    public NetScanDetector? NetScanDetector { get; }
    public SessionExtraData ExtraData { get; }
    public int ProtocolVersion { get; }
    public int TcpConnectWaitCount => _tcpConnectWaitCount;
    public int NetScanErrorCount => _netScanErrorCount;
    public int TcpChannelCount => Tunnel.StreamProxyChannelCount + (_udpChannel != null ? 0 : Tunnel.PacketChannelCount);


    public int UdpConnectionCount => _proxyManager.UdpClientCount;
    public DateTime LastActivityTime => Tunnel.TrafficMeter.LastActivityTime;
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
            UdpBufferSize = options.UdpProxyBufferSizeValue ?? ServerTransportDefaults.UdpProxyBufferSize,
            LogScope = logScope,
            IsPingSupported = true,
            PacketProxyCallbacks = new PacketProxyCallbacks(this),
            AutoDisposePackets = true,
            PacketQueueCapacity = TransportDefaults.ProxyPacketQueueCapacity
        });
        _proxyManager.PacketReceived += Proxy_PacketsReceived;
        _trackingOptions = trackingOptions;
        _maxTcpConnectWaitCount = options.MaxTcpConnectWaitCountValue;
        _maxTcpChannelCount = options.MaxTcpChannelCountValue;
        _streamProxyBufferSize = options.StreamProxyBufferSize ?? ServerTransportDefaults.StreamProxyBufferSize;
        _tcpKernelBufferSize = options.TcpKernelBufferSize;
        _netFilter = netFilter;
        _netScanExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpConnectWaitExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpChannelExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        AllowTcpPacket = options.AllowTcpPacketValue;
        AllowTcpProxy = options.AllowTcpProxyValue;
        ExtraData = extraData;
        VirtualIps = virtualIps;
        Response = sessionResponseEx;
        ProtocolVersion = sessionResponseEx.ProtocolVersion;
        SessionId = sessionResponseEx.SessionId;
        SessionKey = sessionResponseEx.SessionKey ?? throw new InvalidOperationException(
            $"{nameof(sessionResponseEx)} does not have {nameof(sessionResponseEx.SessionKey)}!");

        // the server shapes what it sends, the client's download; what it receives it only enforces, a little
        // looser than the client shapes it, so an honest client is never held
        var maxSpeedMbps = sessionResponseEx.AccessInfo?.MaxSpeedMbps ?? new Traffic();
        Tunnel = new Tunnel(new TunnelOptions {
            MaxPacketChannelCount = options.MaxPacketChannelCountValue,
            PacketQueueCapacity = TransportDefaults.TunnelPacketQueueCapacity,
            AutoDisposePackets = true,
            Mtu = Math.Min(TransportDefaults.MtuServer, extraData.Mtu),
            MaxSpeed = new Traffic(
                sent: maxSpeedMbps.Received * 1_000_000 / 8,
                received: (long)(maxSpeedMbps.Sent * 1_000_000d / 8 * ReceiveSpeedGrace)),
            MaxSpeedBurst = new Traffic(
                sent: TransportDefaults.MaxSpeedBurst,
                received: (long)(TransportDefaults.MaxSpeedBurst * ReceiveSpeedGrace))
        });

        Tunnel.PacketReceived += Tunnel_PacketReceived;

        // ReSharper disable once MergeIntoPattern
        if (options.NetScanLimit != null && options.NetScanTimeout != null)
            NetScanDetector = new NetScanDetector(options.NetScanLimit.Value, options.NetScanTimeout.Value);
    }


    public Traffic UnreportedTraffic {
        get {
            // Intentionally Reversed: sending to tunnel means receiving form client,
            // Intentionally Reversed: receiving from tunnel means sending for client
            var traffic = Tunnel.TrafficMeter.Traffic - _prevTraffic;
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
            ChannelId = Guid.NewGuid().ToString(),
            TrafficMeter = Tunnel.TrafficMeter
        });

        UseUdpChannel = true;
        return _udpChannel.UdpTransport;
    }

    public Traffic TakeUnreportedTraffic()
    {
        lock (_stateLock) {
            // one read of the meter for both: bytes counted between two reads would never be reported
            var meterTraffic = Tunnel.TrafficMeter.Traffic;
            var traffic = meterTraffic - _prevTraffic;
            _prevTraffic = meterTraffic;

            // Intentionally Reversed, as in UnreportedTraffic
            return new Traffic {
                Sent = traffic.Received,
                Received = traffic.Sent
            };
        }
    }

    // closes an open session from the server's side: the first close wins, the server's or the access manager's
    internal bool TryClose(SessionErrorCode errorCode)
    {
        lock (_stateLock) {
            if (Response.ErrorCode != SessionErrorCode.Ok)
                return false;

            Response.ErrorCode = errorCode;
            return true;
        }
    }

    // checked under the same lock as a reply, which may have extended the time or closed the session meanwhile
    internal bool TryExpire(DateTime utcNow)
    {
        lock (_stateLock) {
            var expirationTime = Response.AccessUsage?.ExpirationTime;
            if (Response.ErrorCode != SessionErrorCode.Ok || expirationTime == null || expirationTime >= utcNow)
                return false;

            Response.ErrorCode = SessionErrorCode.SessionExpired;
            return true;
        }
    }

    // a closed session keeps its code: a reply to a request sent before the close cannot reopen it. A reply that
    // leaves it open is stale if its request went before the applied one's, as a status upload's that crossed an ad;
    // a close always applies, since the access managers close for good
    internal bool ApplyResponse(SessionResponse sessionResponse, long requestTimestamp)
    {
        lock (_stateLock) {
            if (Response.ErrorCode != SessionErrorCode.Ok)
                return false;

            if (sessionResponse.ErrorCode == SessionErrorCode.Ok && requestTimestamp < _responseRequestTimestamp)
                return false;

            Response = sessionResponse;
            _responseRequestTimestamp = requestTimestamp;
            return true;
        }
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

        // Tunnel is passthrough, so it just try to put packet on nested queue,
        // if the queue is full, packet will be disposed and drop
        // PacketEnqueue will dispose packets
        Tunnel.SendPacketQueued(ipPacket);
    }

    private void Tunnel_PacketReceived(object? sender, IpPacket ipPacket)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // filter requests; the version is read once, as each read decodes the packet's first byte
        var ipVersion = ipPacket.Version;
        var virtualIp = GetClientVirtualIp(ipVersion);

        // reject if packet source does not match client internal ip
        if (!ipPacket.SourceAddress.Equals(virtualIp)) {
            var ipeEndPointPair = ipPacket.GetEndPoints();
            LogTrack(ipPacket.Protocol, null, ipeEndPointPair.RemoteEndPoint.ToValue(), false, true, "NetFilter");
            _filterBadSourceReporter.Raise();
            throw new NetFilterException(
                $"Invalid tunnel packet source ip. SourceIp: {VhLogger.Format(ipPacket.SourceAddress)}");
        }

        // check TcpPacket
        if (!AllowTcpPacket && ipPacket.Protocol == IpProtocol.Tcp)
            throw new NetFilterException("TcpPacket is not allowed in this session.");

        // filter before mapper because it supposes to filter user requests
        if (_netFilter.IpFilter?.Process(ipPacket.Protocol, ipPacket.GetDestinationEndPoint()) == FilterAction.Block) {
            LogTrack(ipPacket.Protocol, null, ipPacket.GetDestinationEndPoint(), false, true, "NetFilter");
            _filterBlockDestinationReporter.Raise();
            throw new NetFilterException(
                $"Packet discarded due to the NetFilter's policies. DestinationIp: {VhLogger.Format(ipPacket.DestinationAddress)}");
        }

        // Map destination
        if (_netFilter.IpMapper?.ToHost(ipPacket.Protocol, ipPacket.GetDestinationEndPoint(), out var newEndPoint) == true) {
            ipPacket.SetDestinationEndPoint(newEndPoint);
            ipPacket.UpdateAllChecksums();
        }

        // send by the adapter where its NAT translates the version, else by the proxy
        if (_vpnAdapter != null && _vpnAdapter.IsNatSupported(ipVersion) && _vpnAdapter.IsIpVersionSupported(ipVersion))
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
            !_trackingOptions.TrackIcmpValue && protocol is IpProtocol.IcmpV4 or IpProtocol.IcmpV6)
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
            // A token even with the anonymizer off, unlike the client IP in the session log, which is
            // recorded as-is because tracing a connection back to whoever made it is the entire reason that
            // record exists. A destination never serves that purpose — an abuse report already names its
            // target — so all anyone needs here is to tell one destination from another.
            destinationIpStr = _trackingOptions.TrackDestinationIpValue
                ? Redactor.Always.RedactIpAddress(destinationEndPoint.Value.Address)
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

    public async Task ProcessTcpPacketChannelRequest(TcpPacketChannelRequest request,
        IStreamConnection streamConnection, CancellationToken cancellationToken)
    {
        // manage wait count
        Interlocked.Increment(ref _tcpConnectWaitCount);
        using var autoDispose = new AutoDispose(() => Interlocked.Decrement(ref _tcpConnectWaitCount));

        // send OK reply
        await streamConnection.WriteResponseAsync(Response, cancellationToken).Vhc();

        // add channel
        VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
            "Creating a TcpPacketChannel channel. SessionId: {SessionId}", VhLogger.FormatSessionId(SessionId));

        var channel = new StreamPacketChannel(new StreamPacketChannelOptions {
            BufferSize = ServerTransportDefaults.StreamPacketBufferSize,
            RequestTime = request.RequestTime,
            Blocking = false,
            AutoDisposePackets = true,
            StreamConnection = streamConnection,
            ChannelId = request.ChannelId ?? request.RequestId,
            Lifespan = null,
            TrafficMeter = Tunnel.TrafficMeter
        });

        // delete all inactive PacketChannels if ActiveChannelIds exists. InActive channel request time must be less than current request time
        // to avoid deleting active channels when client is creating multiple channels at the same time.
        if (request.ActiveChannelIds != null) {
            var inactiveChannels = Tunnel.PacketChannels
                .Where(x =>
                    x is StreamPacketChannel streamPacketChannel &&
                    streamPacketChannel.RequestTime < request.RequestTime &&
                    !request.ActiveChannelIds.Contains(x.ChannelId));

            // dispose inactive channels
            foreach (var inactiveChannel in inactiveChannels) {
                VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                    "Disposing an inactive PacketChannel. SessionId: {SessionId}, ChannelId: {ChannelId}",
                    VhLogger.FormatSessionId(SessionId), inactiveChannel.ChannelId);
                inactiveChannel.Dispose();
            }
        }

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
                Tunnel.RemoveChannels<IPacketChannel>();
                Tunnel.AddChannel(_udpChannel);
                VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                    "UdpChannel is enabled. SessionId: {SessionId}", SessionId);
            }
            else {
                // disable udp channel
                Tunnel.RemoveChannels<UdpChannel>();
                _udpChannel = null; // remove will dispose the channel, set to null to avoid reuse
                VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                    "UdpChannel is disabled. SessionId: {SessionId}", SessionId);
            }

            field = value;
        }
    }

    internal Task ProcessUdpPacketRequest(UdpPacketRequest request, IStreamConnection streamConnection,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = streamConnection;
        _ = cancellationToken;
        throw new NotImplementedException();
    }

    internal async Task ProcessSessionStatusRequest(SessionStatusRequest request, IStreamConnection streamConnection,
        CancellationToken cancellationToken)
    {
        _ = request;
        await streamConnection.DisposeAsync(Response, cancellationToken).Vhc();
    }

    internal async Task ProcessRewardedAdRequest(RewardedAdRequest request, IStreamConnection streamConnection,
        CancellationToken cancellationToken)
    {
        // it goes at once, not in the reporter's turn, where a slow status upload would use up the ad's request
        // timeout; it is stamped, so a reply to a request sent before it cannot undo it
        var requestTimestamp = Stopwatch.GetTimestamp();
        var sessionResponse = await _accessManager
            .Session_AddUsage(sessionId: SessionId, new Traffic(), adData: request.AdData, cancellationToken).Vhc();
        ApplyResponse(sessionResponse, requestTimestamp);
        await streamConnection.DisposeAsync(Response, cancellationToken).Vhc();
    }

    internal async Task ProcessTcpProxyRequest(StreamProxyChannelRequest request, IStreamConnection streamConnection,
        CancellationToken cancellationToken)
    {
        if (!AllowTcpProxy)
            throw new SessionException(SessionErrorCode.GeneralError, "TcpProxy is not allowed in this session.");

        TcpClient? tcpClientHost = null;
        IStreamConnection? tcpConnectionHost = null;
        var slotReserved = false;
        try {
            // filter before mapper because it supposes to filter user requests
            if (_netFilter.IpFilter?.Process(IpProtocol.Tcp, request.DestinationEndPoint.ToValue()) == FilterAction.Block) {
                LogTrack(IpProtocol.Tcp, null, request.DestinationEndPoint.ToValue(), false, true, "NetFilter");
                _filterBlockDestinationReporter.Raise();
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

            // Apply limitation before create connection to host. It reserves a channel slot on success
            VerifyTcpChannelRequest(streamConnection, request);
            slotReserved = true;

            //set reuseAddress to  true to prevent error only one usage of each socket address is normally permitted
            tcpClientHost = _socketFactory.CreateTcpClient(request.DestinationEndPoint);
            VhUtils.ConfigTcpClient(tcpClientHost,
                sendBufferSize: _tcpKernelBufferSize?.Send,
                receiveBufferSize: _tcpKernelBufferSize?.Receive,
                keepAlive: true,
                noDelay: true);

            // connect to requested destination
            try {
                await tcpClientHost.ConnectAsync(request.DestinationEndPoint, cancellationToken).Vhc();
                tcpConnectionHost = new TcpStreamConnection(tcpClientHost, connectionId: request.RequestId,
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
            await streamConnection.WriteResponseAsync(Response, cancellationToken).Vhc();

            // add the connection
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "Adding a ProxyChannel.");
            var proxyChannel = new ProxyChannel(streamConnection.ToString()!, tcpConnectionHost, streamConnection,
                _streamProxyBufferSize, Tunnel.TrafficMeter);

            // swap the reserved slot with the channel atomically, so a concurrent request can
            // neither count this request twice nor slip in through a gap and overshoot the limit
            lock (_verifyRequestLock) {
                Tunnel.AddChannel(proxyChannel, disposeIfFailed: true);
                Interlocked.Decrement(ref _tcpConnectWaitCount);
                slotReserved = false;
            }
        }
        catch {
            tcpClientHost?.Dispose();
            tcpConnectionHost?.Dispose();
            throw;
        }
        finally {
            if (slotReserved)
                Interlocked.Decrement(ref _tcpConnectWaitCount);
        }
    }

    private void VerifyTcpChannelRequest(IStreamConnection streamConnection, StreamProxyChannelRequest request)
    {
        lock (_verifyRequestLock) {
            // NetScan limit
            VerifyNetScan(IpProtocol.Tcp, request.DestinationEndPoint.ToValue(), request.RequestId);

            // Channel Count limit. Pending connects hold a reserved channel slot, so count them too
            if (TcpChannelCount + _tcpConnectWaitCount >= _maxTcpChannelCount) {
                LogTrack(IpProtocol.Tcp, null, request.DestinationEndPoint.ToValue(), false, true, "MaxTcp");
                _maxTcpChannelExceptionReporter.Raise();
                throw new MaxTcpChannelException(streamConnection.RemoteEndPoint, this, request.RequestId);
            }

            // Check tcp wait limit
            if (TcpConnectWaitCount >= _maxTcpConnectWaitCount) {
                LogTrack(IpProtocol.Tcp, null, request.DestinationEndPoint.ToValue(), false, true, "MaxTcpWait");
                _maxTcpConnectWaitExceptionReporter.Raise();
                throw new MaxTcpConnectWaitException(streamConnection.RemoteEndPoint, this,
                    request.RequestId);
            }

            // passed all limits; reserve a channel slot until the channel is added or the request fails
            Interlocked.Increment(ref _tcpConnectWaitCount);
        }
    }

    private void VerifyNetScan(IpProtocol protocol, IpEndPointValue remoteEndPoint, string requestId)
    {
        if (NetScanDetector == null || NetScanDetector.Verify(remoteEndPoint))
            return;

        LogTrack(protocol, null, remoteEndPoint, false, true, "NetScan");
        _netScanExceptionReporter.Raise();
        Interlocked.Increment(ref _netScanErrorCount);
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
        _filterBlockDestinationReporter.Dispose();
        _filterBadSourceReporter.Dispose();
        NetScanDetector?.Dispose();

        // if there is no reason it is temporary
        var reason = "Cleanup";
        if (Response.ErrorCode != SessionErrorCode.Ok)
            reason = Response.ErrorCode == SessionErrorCode.SessionClosed ? "User" : "Access";

        // Report removing session
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tActor: {Actor,-7}\tSuppressBy: {SuppressedBy,-8}\tErrorCode: {ErrorCode,-20}\tMessage: {message}",
            SessionId, "Close", reason, Response.SuppressedBy, Response.ErrorCode,
            Response.ErrorMessage ?? "None");

        // it must be ended to let manager know that session is disposed and finish all tasks
        DisposedTime = DateTime.UtcNow;
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
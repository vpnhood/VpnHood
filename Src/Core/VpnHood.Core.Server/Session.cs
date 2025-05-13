using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Server.Exceptions;
using VpnHood.Core.Server.Utils;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Server;

public class Session : IAsyncDisposable
{
    private readonly INetFilter _netFilter;
    private readonly IAccessManager _accessManager;
    private readonly IVpnAdapter? _vpnAdapter;
    private readonly ProxyManager _proxyManager;
    private readonly ISocketFactory _socketFactory;
    private readonly object _verifyRequestLock = new();
    private readonly int _maxTcpConnectWaitCount;
    private readonly int _maxTcpChannelCount;
    private readonly int? _tcpBufferSize;
    private readonly int? _tcpKernelSendBufferSize;
    private readonly int? _tcpKernelReceiveBufferSize;
    private readonly TimeSpan _tcpConnectTimeout;
    private readonly TrackingOptions _trackingOptions;
    private IPAddress? _clientInternalIpV6;
    private IPAddress? _clientInternalIpV4;

    [Obsolete]
    private readonly bool _fixClientInternalIp;

    private readonly EventReporter _netScanExceptionReporter = new(VhLogger.Instance,
        "NetScan protector does not allow this request.", GeneralEventId.NetProtect);

    private readonly EventReporter _maxTcpChannelExceptionReporter = new(VhLogger.Instance,
        "Maximum TcpChannel has been reached.", GeneralEventId.NetProtect);

    private readonly EventReporter _maxTcpConnectWaitExceptionReporter = new(VhLogger.Instance,
        "Maximum TcpConnectWait has been reached.", GeneralEventId.NetProtect);

    private readonly EventReporter _filterReporter =
        new(VhLogger.Instance, "Some requests has been blocked.", GeneralEventId.NetProtect);

    private readonly Traffic _prevTraffic = new();
    private int _tcpConnectWaitCount;

    public Tunnel Tunnel { get; }
    public ulong SessionId { get; }
    public byte[] SessionKey { get; }
    public SessionResponse SessionResponseEx { get; internal set; }
    public UdpChannel? UdpChannel => Tunnel.UdpChannel;
    public bool IsDisposed => DisposedTime != null;
    public DateTime? DisposedTime { get; private set; }
    public NetScanDetector? NetScanDetector { get; }
    public SessionExtraData ExtraData { get; }
    public int ProtocolVersion { get; }
    public int TcpConnectWaitCount => _tcpConnectWaitCount;
    public int TcpChannelCount => Tunnel.StreamProxyChannelCount + (Tunnel.IsUdpMode ? 0 : Tunnel.DatagramChannelCount);
    public int UdpConnectionCount => _proxyManager.UdpClientCount;
    public DateTime LastActivityTime => Tunnel.LastActivityTime;
    public VirtualIpBundle VirtualIps { get; }

    internal Session(IAccessManager accessManager,
        IVpnAdapter? vpnAdapter,
        INetFilter netFilter,
        ISocketFactory socketFactory,
        SessionResponseEx sessionResponseEx,
        SessionOptions options,
        TrackingOptions trackingOptions,
        SessionExtraData extraData,
        VirtualIpBundle virtualIps)
    {
        var sessionTuple = Tuple.Create("SessionId", (object?)sessionResponseEx.SessionId);
        var logScope = new LogScope();
        logScope.Data.Add(sessionTuple);

#pragma warning disable CS0612 // Type or member is obsolete
        _fixClientInternalIp = sessionResponseEx.ProtocolVersion < 8;
#pragma warning restore CS0612 // Type or member is obsolete
        _accessManager = accessManager ?? throw new ArgumentNullException(nameof(accessManager));
        _vpnAdapter = vpnAdapter;
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _proxyManager = new ProxyManager(socketFactory, new ProxyManagerOptions {
            UdpTimeout = options.UdpTimeoutValue,
            IcmpTimeout = options.IcmpTimeoutValue,
            MaxUdpClientCount = options.MaxUdpClientCountValue,
            MaxPingClientCount = options.MaxIcmpClientCountValue,
            UdpReceiveBufferSize = options.UdpProxyReceiveBufferSize,
            UdpSendBufferSize = options.UdpProxySendBufferSize,
            LogScope = logScope,
            IsPingSupported = true,
            PacketProxyCallbacks = new PacketProxyCallbacks(this),
            AutoDisposeSentPackets = true,
            PacketQueueCapacity = TunnelDefaults.ProxyPacketQueueCapacity,
            UseUdpProxy2 = options.UseUdpProxy2Value
        });
        _proxyManager.PacketReceived += Proxy_PacketsReceived;
        _trackingOptions = trackingOptions;
        _maxTcpConnectWaitCount = options.MaxTcpConnectWaitCountValue;
        _maxTcpChannelCount = options.MaxTcpChannelCountValue;
        _tcpBufferSize = options.TcpBufferSize;
        _tcpKernelSendBufferSize = options.TcpKernelSendBufferSize;
        _tcpKernelReceiveBufferSize = options.TcpKernelReceiveBufferSize;
        _tcpConnectTimeout = options.TcpConnectTimeoutValue;
        _netFilter = netFilter;
        _netScanExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpConnectWaitExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpChannelExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        ExtraData = extraData;
        VirtualIps = virtualIps;
        SessionResponseEx = sessionResponseEx;
        ProtocolVersion = sessionResponseEx.ProtocolVersion;
        SessionId = sessionResponseEx.SessionId;
        SessionKey = sessionResponseEx.SessionKey ?? throw new InvalidOperationException(
            $"{nameof(sessionResponseEx)} does not have {nameof(sessionResponseEx.SessionKey)}!");
        Tunnel = new Tunnel(new TunnelOptions { MaxDatagramChannelCount = options.MaxDatagramChannelCountValue });
        Tunnel.PacketReceived += Tunnel_PacketReceived;

        // ReSharper disable once MergeIntoPattern
        if (options.NetScanLimit != null && options.NetScanTimeout != null)
            NetScanDetector = new NetScanDetector(options.NetScanLimit.Value, options.NetScanTimeout.Value);
    }

    public Traffic Traffic {
        get {
            lock (_prevTraffic) {
                // Intentionally Reversed: sending to tunnel means receiving form client,
                // Intentionally Reversed: receiving from tunnel means sending for client
                return new Traffic {
                    Sent = Tunnel.Traffic.Received - _prevTraffic.Sent,
                    Received = Tunnel.Traffic.Sent - _prevTraffic.Received
                };
            }
        }
    }

    public Traffic ResetTraffic()
    {
        lock (_prevTraffic) {
            var traffic = Traffic;
            _prevTraffic.Add(traffic);
            return traffic;
        }
    }

    public void SetSyncRequired() => IsSyncRequired = true;
    public bool IsSyncRequired { get; private set; }

    public bool ResetSyncRequired()
    {
        var oldValue = IsSyncRequired;
        IsSyncRequired = false;
        return oldValue;
    }

    public bool UseUdpChannel {
        get => Tunnel.IsUdpMode;
        set {
            if (value == UseUdpChannel)
                return;

            // the udp channel will remove by add stream channel request
            if (!value)
                return;

            // add new channel
            var udpChannel = new UdpChannel(SessionId, SessionKey, true, ProtocolVersion);
            try {
                Tunnel.AddChannel(udpChannel);
            }
            catch {
                udpChannel.DisposeAsync();
            }
        }
    }

    private IPAddress GetClientVirtualIp(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? VirtualIps.IpV4 : VirtualIps.IpV6;
    }

    // todo: legacy version. remove in future
    [Obsolete]
    private IPAddress? GetClientInternalIp(IpVersion ipVersion)
    {
        return ipVersion == IpVersion.IPv4 ? _clientInternalIpV4 : _clientInternalIpV6;
    }

    private void Proxy_PacketsReceived(object sender, PacketReceivedEventArgs eventArgs)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var index = 0; index < eventArgs.IpPackets.Count; index++) {
            var packet = eventArgs.IpPackets[index];
            Proxy_PacketReceived(packet);
        }
    }

    public void Adapter_PacketReceived(IpPacket ipPacket)
    {
        Proxy_PacketReceived(ipPacket);
    }

    private void Proxy_PacketReceived(IpPacket ipPacket)
    {
        if (IsDisposed) return;
        PacketLogger.LogPacket(ipPacket, "Delegating a packet to client...");

        ipPacket = _netFilter.ProcessReply(ipPacket);

#pragma warning disable CS0612 // Type or member is obsolete
        if (_fixClientInternalIp) {
            // fix client internal ip
            // todo: consider using allocated private ip and prevent recalculate checksum
            var clientInternalIp = GetClientInternalIp(ipPacket.Version);
            if (clientInternalIp != null && !ipPacket.DestinationAddress.Equals(clientInternalIp)) {
                ipPacket.DestinationAddress = clientInternalIp;
                ipPacket.UpdateAllChecksums();
            }
        }
#pragma warning restore CS0612 // Type or member is obsolete

        // PacketEnqueue will dispose packets
        Tunnel.SendPacketQueued(ipPacket);
    }

    private readonly List<IpPacket> _adapterPackets = [];
    private readonly object _packetReceivedLock = new();
    private void Tunnel_PacketReceived(object sender, PacketReceivedEventArgs e)
    {
        if (IsDisposed)
            return;

        lock (_packetReceivedLock) {
            // this is the shared resource
            _adapterPackets.Clear();

            // filter requests
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < e.IpPackets.Count; i++) {
                var ipPacket = e.IpPackets[i];
                var virtualIp = GetClientVirtualIp(ipPacket.Version);

                // todo: legacy save caller internal ip at first call
#pragma warning disable CS0612 // Type or member is obsolete
                if (_fixClientInternalIp) {
                    if (ipPacket.Version == IpVersion.IPv4)
                        _clientInternalIpV4 ??= ipPacket.SourceAddress;
                    else if (ipPacket.Version == IpVersion.IPv6)
                        _clientInternalIpV6 ??= ipPacket.SourceAddress;

                    // update source client virtual ip. will be obsolete in future if client set correct ip
                    if (!virtualIp.Equals(ipPacket.SourceAddress)) {
                        // todo: legacy version. Packet must be dropped if it does not have correct source address
                        // PacketLogger.LogPacket(ipPacket, $"Invalid tunnel packet source ip.");
                        ipPacket.SourceAddress = virtualIp;
                        ipPacket.UpdateAllChecksums();
                    }
                }
#pragma warning restore CS0612 // Type or member is obsolete

                // reject if packet source does not match client internal ip
                if (!ipPacket.SourceAddress.Equals(virtualIp)) {
                    PacketLogger.LogPacket(ipPacket, "Invalid tunnel packet source ip.");
                    ipPacket.Dispose();
                    continue;
                }

                // filter
                var ipPacket2 = _netFilter.ProcessRequest(ipPacket);
                if (ipPacket2 == null) {
                    var ipeEndPointPair = ipPacket.GetEndPoints();
                    LogTrack(ipPacket.Protocol.ToString(), null, ipeEndPointPair.RemoteEndPoint, false, true, "NetFilter");
                    _filterReporter.Raise();
                    ipPacket.Dispose();
                    continue;
                }

                // send using tunnel or proxy
                if (_vpnAdapter?.IsIpVersionSupported(ipPacket2.Version) == true)
                    _adapterPackets.Add(ipPacket2);
                else
                    _proxyManager.SendPacketQueued(ipPacket2);

            }

            // send using tunnel or proxy
            _vpnAdapter?.SendPackets(_adapterPackets);
            _adapterPackets.DisposeAllPackets();
        }
    }

    public void LogTrack(string protocol, IPEndPoint? localEndPoint, IPEndPoint? destinationEndPoint,
        bool isNewLocal, bool isNewRemote, string? failReason)
    {
        if (!_trackingOptions.IsEnabled)
            return;

        if (_trackingOptions is { TrackDestinationIpValue: false, TrackDestinationPortValue: false } && !isNewLocal &&
            failReason == null)
            return;

        if (!_trackingOptions.TrackTcpValue && protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase) ||
            !_trackingOptions.TrackUdpValue && protocol.Equals("udp", StringComparison.OrdinalIgnoreCase) ||
            !_trackingOptions.TrackUdpValue && protocol.Equals("icmp", StringComparison.OrdinalIgnoreCase))
            return;

        var mode = (isNewLocal ? "L" : "") + (isNewRemote ? "R" : "");
        var localPortStr = "-";
        var destinationIpStr = "-";
        var destinationPortStr = "-";
        var netScanCount = "-";
        failReason ??= "Ok";

        if (localEndPoint != null)
            localPortStr = _trackingOptions.TrackLocalPortValue ? localEndPoint.Port.ToString() : "*";

        if (destinationEndPoint != null) {
            destinationIpStr = _trackingOptions.TrackDestinationIpValue
                ? VhUtils.RedactIpAddress(destinationEndPoint.Address)
                : "*";
            destinationPortStr = _trackingOptions.TrackDestinationPortValue ? destinationEndPoint.Port.ToString() : "*";
            netScanCount = NetScanDetector?.GetBurstCount(destinationEndPoint).ToString() ?? "*";
        }

        VhLogger.Instance.LogInformation(GeneralEventId.Track,
            "{Proto,-4}\tSessionId {SessionId}\t{Mode,-2}\tTcpCount {TcpCount,4}\tUdpCount {UdpCount,4}\tTcpWait {TcpConnectWaitCount,3}\tNetScan {NetScan,3}\t" +
            "SrcPort {SrcPort,-5}\tDstIp {DstIp,-15}\tDstPort {DstPort,-5}\t{Success,-10}",
            protocol, SessionId, mode,
            TcpChannelCount, _proxyManager.UdpClientCount, _tcpConnectWaitCount, netScanCount,
            localPortStr, destinationIpStr, destinationPortStr, failReason);
    }

    public async Task ProcessTcpDatagramChannelRequest(TcpDatagramChannelRequest request, IClientStream clientStream,
        CancellationToken cancellationToken)
    {
        // send OK reply
        await clientStream.WriteResponse(SessionResponseEx, cancellationToken).VhConfigureAwait();

        // Disable UdpChannel
        UseUdpChannel = false;

        // add channel
        VhLogger.Instance.LogDebug(GeneralEventId.DatagramChannel,
            "Creating a TcpDatagramChannel channel. SessionId: {SessionId}", VhLogger.FormatSessionId(SessionId));

        var channel = new StreamDatagramChannel(clientStream, request.RequestId);
        try {
            Tunnel.AddChannel(channel);
        }
        catch {
            await channel.DisposeAsync().VhConfigureAwait();
            throw;
        }
    }

    public Task ProcessUdpPacketRequest(UdpPacketRequest request, IClientStream clientStream,
        CancellationToken cancellationToken)
    {
        //var udpClient = new UdpClient();
        //udpClient.SendAsync();
        //request.PacketBuffers.
        throw new NotImplementedException();
    }

    public async Task ProcessSessionStatusRequest(SessionStatusRequest request, IClientStream clientStream,
        CancellationToken cancellationToken)
    {
        await clientStream.WriteFinalResponse(SessionResponseEx, cancellationToken).VhConfigureAwait();
    }

    public async Task ProcessRewardedAdRequest(RewardedAdRequest request, IClientStream clientStream,
        CancellationToken cancellationToken)
    {
        SessionResponseEx = await _accessManager
            .Session_AddUsage(sessionId: SessionId, new Traffic(), adData: request.AdData).VhConfigureAwait();
        await clientStream.WriteFinalResponse(SessionResponseEx, cancellationToken).VhConfigureAwait();
    }

    public async Task ProcessTcpProxyRequest(StreamProxyChannelRequest request, IClientStream clientStream,
        CancellationToken cancellationToken)
    {
        var isRequestedEpException = false;
        var isTcpConnectIncreased = false;

        TcpClient? tcpClientHost = null;
        TcpClientStream? tcpClientStreamHost = null;
        StreamProxyChannel? streamProxyChannel = null;
        try {
            // connect to requested site
            VhLogger.Instance.LogDebug(GeneralEventId.StreamProxyChannel,
                $"Connecting to the requested endpoint. RequestedEP: {VhLogger.Format(request.DestinationEndPoint)}");

            // Apply limitation
            VerifyTcpChannelRequest(clientStream, request);

            // prepare client
            Interlocked.Increment(ref _tcpConnectWaitCount);
            isTcpConnectIncreased = true;

            //set reuseAddress to  true to prevent error only one usage of each socket address is normally permitted
            tcpClientHost = _socketFactory.CreateTcpClient(request.DestinationEndPoint);
            VhUtils.ConfigTcpClient(tcpClientHost, _tcpKernelSendBufferSize, _tcpKernelReceiveBufferSize);

            // connect to requested destination
            isRequestedEpException = true;
            await tcpClientHost.VhConnectAsync(request.DestinationEndPoint, _tcpConnectTimeout, cancellationToken)
                .VhConfigureAwait();
            isRequestedEpException = false;

            //tracking
            LogTrack(IpProtocol.Tcp.ToString(), (IPEndPoint)tcpClientHost.Client.LocalEndPoint,
                request.DestinationEndPoint,
                true, true, null);

            // send response
            await clientStream.WriteResponse(SessionResponseEx, cancellationToken).VhConfigureAwait();

            // add the connection
            VhLogger.Instance.LogDebug(GeneralEventId.StreamProxyChannel,
                "Adding a StreamProxyChannel. SessionId: {SessionId}", VhLogger.FormatSessionId(SessionId));

            tcpClientStreamHost =
                new TcpClientStream(tcpClientHost, tcpClientHost.GetStream(), request.RequestId + ":host");
            streamProxyChannel = new StreamProxyChannel(request.RequestId, tcpClientStreamHost, clientStream,
                _tcpBufferSize, _tcpBufferSize);

            Tunnel.AddChannel(streamProxyChannel);
        }
        catch (Exception ex) {
            tcpClientHost?.Dispose();
            if (tcpClientStreamHost != null) await tcpClientStreamHost.DisposeAsync().VhConfigureAwait();
            if (streamProxyChannel != null) await streamProxyChannel.DisposeAsync().VhConfigureAwait();

            if (isRequestedEpException)
                throw new ServerSessionException(clientStream.IpEndPointPair.RemoteEndPoint,
                    this, SessionErrorCode.GeneralError, request.RequestId, ex.Message);

            throw;
        }
        finally {
            if (isTcpConnectIncreased)
                Interlocked.Decrement(ref _tcpConnectWaitCount);
        }
    }

    private void VerifyTcpChannelRequest(IClientStream clientStream, StreamProxyChannelRequest request)
    {
        // filter
        var newEndPoint = _netFilter.ProcessRequest(IpProtocol.Tcp, request.DestinationEndPoint);
        if (newEndPoint == null) {
            LogTrack(IpProtocol.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "NetFilter");
            _filterReporter.Raise();
            throw new RequestBlockedException(clientStream.IpEndPointPair.RemoteEndPoint, this, request.RequestId);
        }

        request.DestinationEndPoint = newEndPoint;

        lock (_verifyRequestLock) {
            // NetScan limit
            VerifyNetScan(IpProtocol.Tcp, request.DestinationEndPoint, request.RequestId);

            // Channel Count limit
            if (TcpChannelCount >= _maxTcpChannelCount) {
                LogTrack(IpProtocol.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "MaxTcp");
                _maxTcpChannelExceptionReporter.Raise();
                throw new MaxTcpChannelException(clientStream.IpEndPointPair.RemoteEndPoint, this, request.RequestId);
            }

            // Check tcp wait limit
            if (TcpConnectWaitCount >= _maxTcpConnectWaitCount) {
                LogTrack(IpProtocol.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "MaxTcpWait");
                _maxTcpConnectWaitExceptionReporter.Raise();
                throw new MaxTcpConnectWaitException(clientStream.IpEndPointPair.RemoteEndPoint, this,
                    request.RequestId);
            }
        }
    }

    private void VerifyNetScan(IpProtocol protocol, IPEndPoint remoteEndPoint, string requestId)
    {
        if (NetScanDetector == null || NetScanDetector.Verify(remoteEndPoint)) return;

        LogTrack(protocol.ToString(), null, remoteEndPoint, false, true, "NetScan");
        _netScanExceptionReporter.Raise();
        throw new NetScanException(remoteEndPoint, this, requestId);
    }

    private readonly AsyncLock _disposeLock = new();

    public async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (IsDisposed) return;
        DisposedTime = DateTime.UtcNow;

        _proxyManager.PacketReceived -= Proxy_PacketsReceived;
        Tunnel.PacketReceived -= Tunnel_PacketReceived;
        await Task.WhenAll(Tunnel.DisposeAsync().AsTask(), _proxyManager.DisposeAsync().AsTask());
        _netScanExceptionReporter.Dispose();
        _maxTcpChannelExceptionReporter.Dispose();
        _maxTcpConnectWaitExceptionReporter.Dispose();

        // if there is no reason it is temporary
        var reason = "Cleanup";
        if (SessionResponseEx.ErrorCode != SessionErrorCode.Ok)
            reason = SessionResponseEx.ErrorCode == SessionErrorCode.SessionClosed ? "User" : "Access";

        // Report removing session
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tActor: {Actor,-7}\tSuppressBy: {SuppressedBy,-8}\tErrorCode: {ErrorCode,-20}\tMessage: {message}",
            SessionId, "Close", reason, SessionResponseEx.SuppressedBy, SessionResponseEx.ErrorCode,
            SessionResponseEx.ErrorMessage ?? "None");
    }

    private class PacketProxyCallbacks(Session session) : IPacketProxyCallbacks
    {
        public void OnConnectionRequested(IpProtocol protocolType, IPEndPoint remoteEndPoint)
        {
            session.VerifyNetScan(protocolType, remoteEndPoint, "OnNewRemoteEndPoint");
        }

        public void OnConnectionEstablished(IpProtocol protocolType, IPEndPoint localEndPoint,
            IPEndPoint remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
            session.LogTrack(protocolType.ToString(), localEndPoint, remoteEndPoint, isNewLocalEndPoint,
                isNewRemoteEndPoint, null);
        }
    }
}
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Server.Exceptions;
using VpnHood.Core.Server.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Factory;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Core.Server;

public class Session : IAsyncDisposable
{
    private readonly INetFilter _netFilter;
    private readonly IAccessManager _accessManager;
    private readonly SessionProxyManager _proxyManager;
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
    public SessionResponse SessionResponse { get; internal set; }
    public UdpChannel? UdpChannel => Tunnel.UdpChannel;
    public bool IsDisposed => DisposedTime != null;
    public DateTime? DisposedTime { get; private set; }
    public NetScanDetector? NetScanDetector { get; }
    public JobSection JobSection { get; }
    public SessionExtraData SessionExtraData { get; }
    public int ProtocolVersion { get; }
    public int TcpConnectWaitCount => _tcpConnectWaitCount;
    public int TcpChannelCount => Tunnel.StreamProxyChannelCount + (Tunnel.IsUdpMode ? 0 : Tunnel.DatagramChannelCount);
    public int UdpConnectionCount => _proxyManager.UdpClientCount;
    public DateTime LastActivityTime => Tunnel.LastActivityTime;
    public IPAddress VirtualIpV4 { get; }
    public IPAddress VirtualIpV6 { get; }
    
    internal Session(IAccessManager accessManager, SessionResponseEx sessionResponse,
        INetFilter netFilter,
        ISocketFactory socketFactory,
        SessionOptions options,
        TrackingOptions trackingOptions,
        SessionExtraData sessionExtraData,
        int protocolVersion,
        ITunProvider? tunProvider,
        IPAddress virtualIpV4,
        IPAddress virtualIpV6)
    {
        var sessionTuple = Tuple.Create("SessionId", (object?)sessionResponse.SessionId);
        var logScope = new LogScope();
        logScope.Data.Add(sessionTuple);

        _accessManager = accessManager ?? throw new ArgumentNullException(nameof(accessManager));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _proxyManager = new SessionProxyManager(this, socketFactory, tunProvider, new ProxyManagerOptions {
            UdpTimeout = options.UdpTimeoutValue,
            IcmpTimeout = options.IcmpTimeoutValue,
            MaxUdpClientCount = options.MaxUdpClientCountValue,
            MaxIcmpClientCount = options.MaxIcmpClientCountValue,
            UdpReceiveBufferSize = options.UdpReceiveBufferSize,
            UdpSendBufferSize = options.UdpSendBufferSize,
            UseUdpProxy2 = options.UseUdpProxy2Value,
            LogScope = logScope
        });
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
        JobSection = new JobSection(options.SyncIntervalValue);
        SessionExtraData = sessionExtraData;
        ProtocolVersion = protocolVersion;
        SessionResponse = sessionResponse;
        VirtualIpV4 = virtualIpV4;
        VirtualIpV6 = virtualIpV6;
        SessionId = sessionResponse.SessionId;
        SessionKey = sessionResponse.SessionKey ?? throw new InvalidOperationException(
            $"{nameof(sessionResponse)} does not have {nameof(sessionResponse.SessionKey)}!");
        Tunnel = new Tunnel(new TunnelOptions { MaxDatagramChannelCount = options.MaxDatagramChannelCountValue });
        Tunnel.PacketReceived += Tunnel_OnPacketReceived;

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

    private IPAddress GetClientVirtualIp(IPVersion ipVersion)
    {
        return ipVersion == IPVersion.IPv4 ? VirtualIpV4 : VirtualIpV6;
    }

    // todo: legacy version. remove in future
    private IPAddress? GetClientInternalIp(IPVersion ipVersion)
    {
        return ipVersion == IPVersion.IPv4 ? _clientInternalIpV4 : _clientInternalIpV6;
    }

    public Task ProcessInboundPacket(IPPacket ipPacket)
    {
        if (VhLogger.IsDiagnoseMode)
            PacketUtil.LogPacket(ipPacket, "Delegating packet to client via proxy.");

        ipPacket = _netFilter.ProcessReply(ipPacket);

        // fix client internal ip
        // todo: consider using allocated private ip and prevent recalculate checksum
        var clientInternalIp = GetClientInternalIp(ipPacket.Version);
        if (clientInternalIp != null && !ipPacket.DestinationAddress.Equals(clientInternalIp)) {
            ipPacket.DestinationAddress = clientInternalIp;
            if (ipPacket.Protocol == ProtocolType.IcmpV6)
                PacketUtil.UpdateIpPacket(ipPacket); //IPv6 ICMP checksum need to be recalculated if destination address changed
            else
                PacketUtil.UpdateIpChecksum(ipPacket); //IPv4 header checksum need to be recalculated if destination address changed
        }

        return Tunnel.SendPacketAsync(ipPacket, CancellationToken.None);
    }

    private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
    {
        if (IsDisposed)
            return;

        // filter requests
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < e.IpPackets.Count; i++) {
            var ipPacket = e.IpPackets[i];

            // todo: legacy save caller internal ip at first call
            if (ipPacket.Version == IPVersion.IPv4) 
                _clientInternalIpV4 ??= ipPacket.SourceAddress;
            else if (ipPacket.Version == IPVersion.IPv6) 
                _clientInternalIpV6 ??= ipPacket.SourceAddress;

            // update source client virtual ip. will be obsolete in future if client set correct ip
            var virtualIp = GetClientVirtualIp(ipPacket.Version);
            if (!virtualIp.Equals(ipPacket.SourceAddress)) {
                // todo: legacy version. Packet must be dropped if it does not have correct source address
                // PacketUtil.LogPacket(ipPacket, $"Invalid tunnel packet source ip.");
                ipPacket.SourceAddress = virtualIp; 
                PacketUtil.UpdateIpChecksum(ipPacket);

            }

            // filter
            var ipPacket2 = _netFilter.ProcessRequest(ipPacket);
            if (ipPacket2 == null) {
                var ipeEndPointPair = PacketUtil.GetPacketEndPoints(ipPacket);
                LogTrack(ipPacket.Protocol.ToString(), null, ipeEndPointPair.RemoteEndPoint, false, true, "NetFilter");
                _filterReporter.Raise();
                continue;
            }

            _ = _proxyManager.SendPacket(ipPacket2);
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
                ? VhUtil.RedactIpAddress(destinationEndPoint.Address)
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
        await clientStream.WriteResponse(SessionResponse, cancellationToken).VhConfigureAwait();

        // Disable UdpChannel
        UseUdpChannel = false;

        // add channel
        VhLogger.Instance.LogTrace(GeneralEventId.DatagramChannel,
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
        await clientStream.WriteFinalResponse(SessionResponse, cancellationToken).VhConfigureAwait();
    }

    public async Task ProcessRewardedAdRequest(RewardedAdRequest request, IClientStream clientStream,
        CancellationToken cancellationToken)
    {
        SessionResponse = await _accessManager.Session_AddUsage(sessionId: SessionId, new Traffic(), adData: request.AdData).VhConfigureAwait();
        await clientStream.WriteFinalResponse(SessionResponse, cancellationToken).VhConfigureAwait();
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
            VhLogger.Instance.LogTrace(GeneralEventId.StreamProxyChannel,
                $"Connecting to the requested endpoint. RequestedEP: {VhLogger.Format(request.DestinationEndPoint)}");

            // Apply limitation
            VerifyTcpChannelRequest(clientStream, request);

            // prepare client
            Interlocked.Increment(ref _tcpConnectWaitCount);
            isTcpConnectIncreased = true;

            //set reuseAddress to  true to prevent error only one usage of each socket address is normally permitted
            tcpClientHost = _socketFactory.CreateTcpClient(request.DestinationEndPoint.AddressFamily);
            _socketFactory.SetKeepAlive(tcpClientHost.Client, true);
            VhUtil.ConfigTcpClient(tcpClientHost, _tcpKernelSendBufferSize, _tcpKernelReceiveBufferSize);

            // connect to requested destination
            isRequestedEpException = true;
            await tcpClientHost.VhConnectAsync(request.DestinationEndPoint, _tcpConnectTimeout, cancellationToken).VhConfigureAwait();
            isRequestedEpException = false;

            //tracking
            LogTrack(ProtocolType.Tcp.ToString(), (IPEndPoint)tcpClientHost.Client.LocalEndPoint,
                request.DestinationEndPoint,
                true, true, null);

            // send response
            await clientStream.WriteResponse(SessionResponse, cancellationToken).VhConfigureAwait();

            // add the connection
            VhLogger.Instance.LogTrace(GeneralEventId.StreamProxyChannel,
                "Adding a StreamProxyChannel. SessionId: {SessionId}", VhLogger.FormatSessionId(SessionId));

            tcpClientStreamHost = new TcpClientStream(tcpClientHost, tcpClientHost.GetStream(), request.RequestId + ":host");
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
        var newEndPoint = _netFilter.ProcessRequest(ProtocolType.Tcp, request.DestinationEndPoint);
        if (newEndPoint == null) {
            LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "NetFilter");
            _filterReporter.Raise();
            throw new RequestBlockedException(clientStream.IpEndPointPair.RemoteEndPoint, this, request.RequestId);
        }

        request.DestinationEndPoint = newEndPoint;

        lock (_verifyRequestLock) {
            // NetScan limit
            VerifyNetScan(ProtocolType.Tcp, request.DestinationEndPoint, request.RequestId);

            // Channel Count limit
            if (TcpChannelCount >= _maxTcpChannelCount) {
                LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "MaxTcp");
                _maxTcpChannelExceptionReporter.Raise();
                throw new MaxTcpChannelException(clientStream.IpEndPointPair.RemoteEndPoint, this, request.RequestId);
            }

            // Check tcp wait limit
            if (TcpConnectWaitCount >= _maxTcpConnectWaitCount) {
                LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "MaxTcpWait");
                _maxTcpConnectWaitExceptionReporter.Raise();
                throw new MaxTcpConnectWaitException(clientStream.IpEndPointPair.RemoteEndPoint, this,
                    request.RequestId);
            }
        }
    }

    private void VerifyNetScan(ProtocolType protocol, IPEndPoint remoteEndPoint, string requestId)
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

        Tunnel.PacketReceived -= Tunnel_OnPacketReceived;
        await Task.WhenAll(Tunnel.DisposeAsync().AsTask(), _proxyManager.DisposeAsync().AsTask());
        _netScanExceptionReporter.Dispose();
        _maxTcpChannelExceptionReporter.Dispose();
        _maxTcpConnectWaitExceptionReporter.Dispose();

        // if there is no reason it is temporary
        var reason = "Cleanup";
        if (SessionResponse.ErrorCode != SessionErrorCode.Ok)
            reason = SessionResponse.ErrorCode == SessionErrorCode.SessionClosed ? "User" : "Access";

        // Report removing session
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tActor: {Actor,-7}\tSuppressBy: {SuppressedBy,-8}\tErrorCode: {ErrorCode,-20}\tMessage: {message}",
            SessionId, "Close", reason, SessionResponse.SuppressedBy, SessionResponse.ErrorCode,
            SessionResponse.ErrorMessage ?? "None");
    }

    private class SessionProxyManager(
        Session session,
        ISocketFactory socketFactory,
        ITunProvider? tunProvider,
        ProxyManagerOptions options)
        : ProxyManager(socketFactory, options)
    {
        protected override bool IsPingSupported => true;

        public override Task OnPacketReceived(IPPacket ipPacket)
        {
            return session.ProcessInboundPacket(ipPacket);
        }

        public override Task SendPacket(IPPacket ipPacket)
        {
            if (tunProvider != null) {
                tunProvider.SendPacket(ipPacket);
                return Task.CompletedTask;
            }

            return base.SendPacket(ipPacket);
        }

        public override void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint,
            IPEndPoint remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
            session.LogTrack(protocolType.ToString(), localEndPoint, remoteEndPoint, isNewLocalEndPoint,
                isNewRemoteEndPoint, null);
        }

        public override void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint)
        {
            session.VerifyNetScan(protocolType, remoteEndPoint, "OnNewRemoteEndPoint");
        }
    }
}
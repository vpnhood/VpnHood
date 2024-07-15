using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.ApiClients;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers;
using VpnHood.Server.Access.Messaging;
using VpnHood.Server.Exceptions;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Channels;
using VpnHood.Tunneling.ClientStreams;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;
using VpnHood.Tunneling.Utils;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Server;

public class Session : IAsyncDisposable, IJob
{
    private readonly INetFilter _netFilter;
    private readonly IAccessManager _accessManager;
    private readonly SessionProxyManager _proxyManager;
    private readonly ISocketFactory _socketFactory;
    private readonly AsyncLock _syncLock = new();
    private readonly object _verifyRequestLock = new();
    private readonly int _maxTcpConnectWaitCount;
    private readonly int _maxTcpChannelCount;
    private readonly int? _tcpBufferSize;
    private readonly int? _tcpKernelSendBufferSize;
    private readonly int? _tcpKernelReceiveBufferSize;
    private readonly long _syncCacheSize;
    private readonly TimeSpan _tcpConnectTimeout;
    private readonly TrackingOptions _trackingOptions;
    private readonly EventReporter _netScanExceptionReporter = new(VhLogger.Instance, "NetScan protector does not allow this request.", GeneralEventId.NetProtect);
    private readonly EventReporter _maxTcpChannelExceptionReporter = new(VhLogger.Instance, "Maximum TcpChannel has been reached.", GeneralEventId.NetProtect);
    private readonly EventReporter _maxTcpConnectWaitExceptionReporter = new(VhLogger.Instance, "Maximum TcpConnectWait has been reached.", GeneralEventId.NetProtect);
    private readonly EventReporter _filterReporter = new(VhLogger.Instance, "Some requests has been blocked.", GeneralEventId.NetProtect);
    private readonly Traffic _syncTraffic = new();
    private int _tcpConnectWaitCount;

    public Tunnel Tunnel { get; }
    public ulong SessionId { get; }
    public byte[] SessionKey { get; }
    public SessionResponse SessionResponse { get; private set; }
    public UdpChannel? UdpChannel => Tunnel.UdpChannel;
    public bool IsDisposed { get; private set; }
    public NetScanDetector? NetScanDetector { get; }
    public JobSection JobSection { get; }
    public SessionExtraData SessionExtraData { get; }
    public int TcpConnectWaitCount => _tcpConnectWaitCount;
    public int TcpChannelCount => Tunnel.StreamProxyChannelCount + (Tunnel.IsUdpMode ? 0 : Tunnel.DatagramChannelCount);
    public int UdpConnectionCount => _proxyManager.UdpClientCount;
    public DateTime LastActivityTime => Tunnel.LastActivityTime;

    internal Session(IAccessManager accessManager, SessionResponseEx sessionResponse,
        INetFilter netFilter,
        ISocketFactory socketFactory,
        SessionOptions options, TrackingOptions trackingOptions,
        SessionExtraData sessionExtraData)
    {
        var sessionTuple = Tuple.Create("SessionId", (object?)sessionResponse.SessionId);
        var logScope = new LogScope();
        logScope.Data.Add(sessionTuple);

        _accessManager = accessManager ?? throw new ArgumentNullException(nameof(accessManager));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _proxyManager = new SessionProxyManager(this, socketFactory, new ProxyManagerOptions
        {
            UdpTimeout = options.UdpTimeoutValue,
            IcmpTimeout = options.IcmpTimeoutValue,
            MaxUdpClientCount = options.MaxUdpClientCountValue,
            MaxIcmpClientCount = options.MaxIcmpClientCountValue,
            UseUdpProxy2 = options.UseUdpProxy2Value,
            LogScope = logScope
        });
        _trackingOptions = trackingOptions;
        _maxTcpConnectWaitCount = options.MaxTcpConnectWaitCountValue;
        _maxTcpChannelCount = options.MaxTcpChannelCountValue;
        _tcpBufferSize = options.TcpBufferSize;
        _tcpKernelSendBufferSize = options.TcpKernelSendBufferSize;
        _tcpKernelReceiveBufferSize = options.TcpKernelReceiveBufferSize;
        _syncCacheSize = options.SyncCacheSizeValue;
        _tcpConnectTimeout = options.TcpConnectTimeoutValue;
        _netFilter = netFilter;
        _netScanExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpConnectWaitExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpChannelExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        JobSection = new JobSection(options.SyncIntervalValue);
        SessionExtraData = sessionExtraData;
        SessionResponse = sessionResponse;
        SessionId = sessionResponse.SessionId;
        SessionKey = sessionResponse.SessionKey ?? throw new InvalidOperationException($"{nameof(sessionResponse)} does not have {nameof(sessionResponse.SessionKey)}!");
        Tunnel = new Tunnel(new TunnelOptions { MaxDatagramChannelCount = options.MaxDatagramChannelCountValue });
        Tunnel.PacketReceived += Tunnel_OnPacketReceived;

        // ReSharper disable once MergeIntoPattern
        if (options.NetScanLimit != null && options.NetScanTimeout != null)
            NetScanDetector = new NetScanDetector(options.NetScanLimit.Value, options.NetScanTimeout.Value);

        JobRunner.Default.Add(this);
    }

    public Task RunJob()
    {
        return IsDisposed 
            ? Task.CompletedTask 
            : Sync(true, false);
    }

    public bool UseUdpChannel
    {
        get => Tunnel.IsUdpMode;
        set
        {
            if (value == UseUdpChannel)
                return;

            // the udp channel will remove by add stream channel request
            if (!value)
                return;

            // add new channel
            var udpChannel = new UdpChannel(SessionId, SessionKey, true, SessionExtraData.ProtocolVersion);
            try
            {
                Tunnel.AddChannel(udpChannel);
            }
            catch
            {
                udpChannel.DisposeAsync();
            }
        }
    }

    private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
    {
        if (IsDisposed)
            return;

        // filter requests
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < e.IpPackets.Length; i++)
        {
            var ipPacket = e.IpPackets[i];
            var ipPacket2 = _netFilter.ProcessRequest(ipPacket);
            if (ipPacket2 == null)
            {
                var ipeEndPointPair = PacketUtil.GetPacketEndPoints(ipPacket);
                LogTrack(ipPacket.Protocol.ToString(), null, ipeEndPointPair.RemoteEndPoint, false, true, "NetFilter");
                _filterReporter.Raise();
                continue;
            }

            _ = _proxyManager.SendPacket(ipPacket2);
        }
    }

    public Task Sync()
    {
        return Sync(true, false);
    }

    private async Task Sync(bool force, bool closeSession, string? adData = null)
    {
        using var syncLock = await _syncLock.LockAsync().VhConfigureAwait();
        if (SessionResponse.ErrorCode != SessionErrorCode.Ok)
            return;

        // prepare scope
        using var scope = VhLogger.Instance.BeginScope(
            $"Server => SessionId: {VhLogger.FormatSessionId(SessionId)}");

        // calculate traffic
        var traffic = new Traffic
        {
            Sent = Tunnel.Traffic.Received - _syncTraffic.Sent, // Intentionally Reversed: sending to tunnel means receiving form client,
            Received = Tunnel.Traffic.Sent - _syncTraffic.Received // Intentionally Reversed: receiving from tunnel means sending for client
        };

        var shouldSync = closeSession || force || traffic.Total >= _syncCacheSize;
        if (!shouldSync)
            return;

        // reset usage and sync time; no matter it is successful or not to prevent frequent call
        _syncTraffic.Add(traffic);

        try
        {
            SessionResponse = closeSession
                ? await _accessManager.Session_Close(SessionId, traffic).VhConfigureAwait()
                : await _accessManager.Session_AddUsage(SessionId, traffic, adData).VhConfigureAwait();

            // dispose for any error
            if (SessionResponse.ErrorCode != SessionErrorCode.Ok)
                await DisposeAsync(false, false).VhConfigureAwait();
        }
        catch (ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            SessionResponse.ErrorCode = SessionErrorCode.AccessError;
            SessionResponse.ErrorMessage = "Session Not Found.";
            await DisposeAsync(false, false).VhConfigureAwait();
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(GeneralEventId.AccessManager, ex,
                "Could not report usage to the access-server.");
        }
    }

    public void LogTrack(string protocol, IPEndPoint? localEndPoint, IPEndPoint? destinationEndPoint,
        bool isNewLocal, bool isNewRemote, string? failReason)
    {
        if (!_trackingOptions.IsEnabled)
            return;

        if (_trackingOptions is { TrackDestinationIpValue: false, TrackDestinationPortValue: false } && !isNewLocal && failReason == null)
            return;

        if (!_trackingOptions.TrackTcpValue && protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase) ||
            !_trackingOptions.TrackUdpValue && protocol.Equals("udp", StringComparison.OrdinalIgnoreCase) ||
            !_trackingOptions.TrackUdpValue && protocol.Equals("icmp", StringComparison.OrdinalIgnoreCase))
            return;

        var mode = (isNewLocal ? "L" : "") + ((isNewRemote ? "R" : ""));
        var localPortStr = "-";
        var destinationIpStr = "-";
        var destinationPortStr = "-";
        var netScanCount = "-";
        failReason ??= "Ok";

        if (localEndPoint != null)
            localPortStr = _trackingOptions.TrackLocalPortValue ? localEndPoint.Port.ToString() : "*";

        if (destinationEndPoint != null)
        {
            destinationIpStr = _trackingOptions.TrackDestinationIpValue ? VhUtil.RedactIpAddress(destinationEndPoint.Address) : "*";
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

    public async Task ProcessTcpDatagramChannelRequest(TcpDatagramChannelRequest request, IClientStream clientStream, CancellationToken cancellationToken)
    {
        // send OK reply
        await StreamUtil.WriteJsonAsync(clientStream.Stream, SessionResponse, cancellationToken).VhConfigureAwait();

        // Disable UdpChannel
        UseUdpChannel = false;

        // add channel
        VhLogger.Instance.LogTrace(GeneralEventId.DatagramChannel,
            "Creating a TcpDatagramChannel channel. SessionId: {SessionId}", VhLogger.FormatSessionId(SessionId));

        var channel = new StreamDatagramChannel(clientStream, request.RequestId);
        try
        {
            Tunnel.AddChannel(channel);
        }
        catch
        {
            await channel.DisposeAsync().VhConfigureAwait();
            throw;
        }
    }

    public Task ProcessUdpPacketRequest(UdpPacketRequest request, IClientStream clientStream, CancellationToken cancellationToken)
    {
        //var udpClient = new UdpClient();
        //udpClient.SendAsync();
        //request.PacketBuffers.
        throw new NotImplementedException();
    }

    public async Task ProcessSessionStatusRequest(SessionStatusRequest request, IClientStream clientStream, CancellationToken cancellationToken)
    {
        await StreamUtil.WriteJsonAsync(clientStream.Stream, SessionResponse, cancellationToken).VhConfigureAwait();
        await clientStream.DisposeAsync().VhConfigureAwait();
    }

    public async Task ProcessAdRewardRequest(AdRewardRequest request, IClientStream clientStream, CancellationToken cancellationToken)
    {
        await Sync(force: true, closeSession: false, adData: request.AdData).VhConfigureAwait();
        await StreamUtil.WriteJsonAsync(clientStream.Stream, SessionResponse, cancellationToken).VhConfigureAwait();
        await clientStream.DisposeAsync().VhConfigureAwait();
    }

    public async Task ProcessTcpProxyRequest(StreamProxyChannelRequest request, IClientStream clientStream, CancellationToken cancellationToken)
    {
        var isRequestedEpException = false;
        var isTcpConnectIncreased = false;

        TcpClient? tcpClientHost = null;
        TcpClientStream? tcpClientStreamHost = null;
        StreamProxyChannel? streamProxyChannel = null;
        try
        {
            // connect to requested site
            VhLogger.Instance.LogTrace(GeneralEventId.StreamProxyChannel,
                $"Connecting to the requested endpoint. RequestedEP: {VhLogger.Format(request.DestinationEndPoint)}");

            // Apply limitation
            VerifyTcpChannelRequest(clientStream, request);

            // prepare client
            Interlocked.Increment(ref _tcpConnectWaitCount);
            isTcpConnectIncreased = true;

            //set reuseAddress to  true to prevent error "Only one usage of each socket address is normally permitted
            tcpClientHost = _socketFactory.CreateTcpClient(request.DestinationEndPoint.AddressFamily);
            _socketFactory.SetKeepAlive(tcpClientHost.Client, true);
            VhUtil.ConfigTcpClient(tcpClientHost, _tcpKernelSendBufferSize, _tcpKernelReceiveBufferSize);

            // connect to requested destination
            isRequestedEpException = true;
            await VhUtil.RunTask(
                tcpClientHost.ConnectAsync(request.DestinationEndPoint.Address, request.DestinationEndPoint.Port),
                _tcpConnectTimeout, cancellationToken)
                .VhConfigureAwait();
            isRequestedEpException = false;

            //tracking
            LogTrack(ProtocolType.Tcp.ToString(), (IPEndPoint)tcpClientHost.Client.LocalEndPoint, request.DestinationEndPoint,
                true, true, null);

            // send response
            await StreamUtil.WriteJsonAsync(clientStream.Stream, SessionResponse, cancellationToken).VhConfigureAwait();

            // add the connection
            VhLogger.Instance.LogTrace(GeneralEventId.StreamProxyChannel,
                "Adding a StreamProxyChannel. SessionId: {SessionId}", VhLogger.FormatSessionId(SessionId));

            tcpClientStreamHost = new TcpClientStream(tcpClientHost, tcpClientHost.GetStream(), request.RequestId + ":host");
            streamProxyChannel = new StreamProxyChannel(request.RequestId, tcpClientStreamHost, clientStream, _tcpBufferSize, _tcpBufferSize);

            Tunnel.AddChannel(streamProxyChannel);
        }
        catch (Exception ex)
        {
            tcpClientHost?.Dispose();
            if (tcpClientStreamHost != null) await tcpClientStreamHost.DisposeAsync().VhConfigureAwait();
            if (streamProxyChannel != null) await streamProxyChannel.DisposeAsync().VhConfigureAwait();

            if (isRequestedEpException)
                throw new ServerSessionException(clientStream.IpEndPointPair.RemoteEndPoint,
                    this, SessionErrorCode.GeneralError, request.RequestId, ex.Message);

            throw;
        }
        finally
        {
            if (isTcpConnectIncreased)
                Interlocked.Decrement(ref _tcpConnectWaitCount);
        }
    }

    private void VerifyTcpChannelRequest(IClientStream clientStream, StreamProxyChannelRequest request)
    {
        // filter
        var newEndPoint = _netFilter.ProcessRequest(ProtocolType.Tcp, request.DestinationEndPoint);
        if (newEndPoint == null)
        {
            LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "NetFilter");
            _filterReporter.Raise();
            throw new RequestBlockedException(clientStream.IpEndPointPair.RemoteEndPoint, this, request.RequestId);
        }
        request.DestinationEndPoint = newEndPoint;

        lock (_verifyRequestLock)
        {
            // NetScan limit
            VerifyNetScan(ProtocolType.Tcp, request.DestinationEndPoint, request.RequestId);

            // Channel Count limit
            if (TcpChannelCount >= _maxTcpChannelCount)
            {
                LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "MaxTcp");
                _maxTcpChannelExceptionReporter.Raise();
                throw new MaxTcpChannelException(clientStream.IpEndPointPair.RemoteEndPoint, this, request.RequestId);
            }

            // Check tcp wait limit
            if (TcpConnectWaitCount >= _maxTcpConnectWaitCount)
            {
                LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "MaxTcpWait");
                _maxTcpConnectWaitExceptionReporter.Raise();
                throw new MaxTcpConnectWaitException(clientStream.IpEndPointPair.RemoteEndPoint, this, request.RequestId);
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

    public ValueTask Close()
    {
        return DisposeAsync(true, true);
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true, false);
    }

    private async ValueTask DisposeAsync(bool sync, bool byUser)
    {
        if (IsDisposed) return;
        IsDisposed = true;

        // Sync must before dispose, Some dispose may take time
        if (sync)
            await Sync(true, byUser).VhConfigureAwait();

        Tunnel.PacketReceived -= Tunnel_OnPacketReceived;
        _ = Tunnel.DisposeAsync();
        _ = _proxyManager.DisposeAsync();
        _netScanExceptionReporter.Dispose();
        _maxTcpChannelExceptionReporter.Dispose();
        _maxTcpConnectWaitExceptionReporter.Dispose();

        // if there is no reason it is temporary
        var reason = "Cleanup";
        if (SessionResponse.ErrorCode != SessionErrorCode.Ok)
            reason = byUser ? "User" : "Access";

        // Report removing session
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tActor: {Actor,-7}\tSuppressBy: {SuppressedBy,-8}\tErrorCode: {ErrorCode,-20}\tMessage: {message}",
            SessionId, "Close", reason, SessionResponse.SuppressedBy, SessionResponse.ErrorCode, SessionResponse.ErrorMessage ?? "None");
    }

    private class SessionProxyManager(Session session, ISocketFactory socketFactory, ProxyManagerOptions options)
        : ProxyManager(socketFactory, options)
    {
        protected override bool IsPingSupported => true;

        public override Task OnPacketReceived(IPPacket ipPacket)
        {
            if (VhLogger.IsDiagnoseMode)
                PacketUtil.LogPacket(ipPacket, "Delegating packet to client via proxy.");

            ipPacket = session._netFilter.ProcessReply(ipPacket);
            return session.Tunnel.SendPacketAsync(ipPacket, CancellationToken.None);
        }

        public override void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
            session.LogTrack(protocolType.ToString(), localEndPoint, remoteEndPoint, isNewLocalEndPoint, isNewRemoteEndPoint, null);
        }

        public override void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint)
        {
            session.VerifyNetScan(protocolType, remoteEndPoint, "OnNewRemoteEndPoint");
        }
    }
}
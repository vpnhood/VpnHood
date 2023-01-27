using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Client;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Exceptions;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Server;

public class Session : IAsyncDisposable, IJob
{
    private readonly IAccessServer _accessServer;
    private readonly SessionProxyManager _proxyManager;
    private readonly ISocketFactory _socketFactory;
    private readonly IPEndPoint _localEndPoint;
    private readonly object _syncLock = new();
    private readonly object _verifyRequestLock = new();
    private readonly int _maxTcpConnectWaitCount;
    private readonly int _maxTcpChannelCount;
    private readonly int? _tcpBufferSize;
    private readonly TimeSpan _tcpTimeout;
    private readonly long _syncCacheSize;
    private readonly TimeSpan _tcpConnectTimeout;
    private readonly TrackingOptions _trackingOptions;
    private readonly EventReporter _netScanExceptionReporter = new(VhLogger.Instance, "NetScan protector does not allow this request.", GeneralEventId.NetProtect);
    private readonly EventReporter _maxTcpChannelExceptionReporter = new(VhLogger.Instance, "Maximum TcpChannel has been reached.", GeneralEventId.NetProtect);
    private readonly EventReporter _maxTcpConnectWaitExceptionReporter = new(VhLogger.Instance, "Maximum TcpConnectWait has been reached.", GeneralEventId.NetProtect);
    private bool _isSyncing;
    private long _syncReceivedTraffic;
    private long _syncSentTraffic;
    private int _tcpConnectWaitCount;

    public Tunnel Tunnel { get; }
    public uint SessionId { get; }
    public byte[] SessionKey { get; }
    public SessionResponseBase SessionResponse { get; private set; }
    public UdpChannel? UdpChannel { get; private set; }
    public bool IsDisposed { get; private set; }
    public NetScanDetector? NetScanDetector { get; }
    public JobSection JobSection { get; }
    public HelloRequest? HelloRequest { get; }
    public int TcpConnectWaitCount => _tcpConnectWaitCount;
    public int TcpChannelCount => Tunnel.StreamChannelCount + (UseUdpChannel ? 0 : Tunnel.DatagramChannels.Length);
    public int UdpConnectionCount => _proxyManager.UdpClientCount + (UseUdpChannel ? 1 : 0);
    public DateTime LastActivityTime => Tunnel.LastActivityTime;

    internal Session(IAccessServer accessServer, SessionResponse sessionResponse, SocketFactory socketFactory,
        IPEndPoint localEndPoint, SessionOptions options, TrackingOptions trackingOptions, HelloRequest? helloRequest)
    {
        var sessionTuple = Tuple.Create("SessionId", (object?)sessionResponse.SessionId);
        var logScope = new LogScope();
        logScope.Data.Add(sessionTuple);

        _accessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _proxyManager = new SessionProxyManager(this, socketFactory, new ProxyManagerOptions
        {
            UdpTimeout = options.UdpTimeout,
            IcmpTimeout = options.IcmpTimeout,
            MaxUdpWorkerCount = options.MaxUdpPortCount,
            UseUdpProxy2 = options.UseUdpProxy2,
            LogScope = logScope
        });
        _localEndPoint = localEndPoint;
        _trackingOptions = trackingOptions;
        _maxTcpConnectWaitCount = options.MaxTcpConnectWaitCount;
        _maxTcpChannelCount = options.MaxTcpChannelCount;
        _tcpBufferSize = options.TcpBufferSize;
        _syncCacheSize = options.SyncCacheSize;
        _tcpTimeout = options.TcpTimeout;
        _tcpConnectTimeout = options.TcpConnectTimeout;
        _netScanExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpConnectWaitExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        _maxTcpChannelExceptionReporter.LogScope.Data.AddRange(logScope.Data);
        HelloRequest = helloRequest;
        SessionResponse = new SessionResponseBase(sessionResponse);
        SessionId = sessionResponse.SessionId;
        SessionKey = sessionResponse.SessionKey ?? throw new InvalidOperationException($"{nameof(sessionResponse)} does not have {nameof(sessionResponse.SessionKey)}!");
        JobSection = new JobSection(options.SyncInterval);

        var tunnelOptions = new TunnelOptions();
        if (options.MaxDatagramChannelCount is > 0) tunnelOptions.MaxDatagramChannelCount = options.MaxDatagramChannelCount.Value;
        Tunnel = new Tunnel(tunnelOptions);
        Tunnel.OnPacketReceived += Tunnel_OnPacketReceived;

        if (options.NetScanLimit != null && options.NetScanTimeout != null)
            NetScanDetector = new NetScanDetector(options.NetScanLimit.Value, options.NetScanTimeout.Value);

        JobRunner.Default.Add(this);
    }

    public Task RunJob()
    {
        return Sync(true, false);
    }

    public bool UseUdpChannel
    {
        get => Tunnel.DatagramChannels.Length == 1 && Tunnel.DatagramChannels[0] is UdpChannel;
        set
        {
            if (value == UseUdpChannel)
                return;

            if (value)
            {
                // remove tcpDatagram channels
                foreach (var item in Tunnel.DatagramChannels.Where(x => x != UdpChannel))
                    Tunnel.RemoveChannel(item);

                // create UdpKey
                using var aes = Aes.Create();
                aes.KeySize = 128;
                aes.GenerateKey();

                // Create the only one UdpChannel
                UdpChannel = new UdpChannel(false, _socketFactory.CreateUdpClient(_localEndPoint.AddressFamily), SessionId, aes.Key);
                try { Tunnel.AddChannel(UdpChannel); }
                catch { UdpChannel.Dispose(); throw; }
            }
            else
            {
                // remove udp channels
                foreach (var item in Tunnel.DatagramChannels.Where(x => x == UdpChannel))
                    Tunnel.RemoveChannel(item);
                UdpChannel = null;
            }
        }
    }

    private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
    {
        if (IsDisposed)
            return;

        _proxyManager.SendPacket(e.IpPackets);
    }

    public Task Sync() => Sync(true, false);

    private async Task Sync(bool force, bool closeSession)
    {
        if (SessionResponse.ErrorCode != SessionErrorCode.Ok)
            return;

        using var scope = VhLogger.Instance.BeginScope(
            $"Server => SessionId: {VhLogger.FormatSessionId(SessionId)}, TokenId: {VhLogger.FormatId(HelloRequest?.TokenId)}");

        UsageInfo usageParam;
        lock (_syncLock)
        {
            if (_isSyncing)
                return;

            usageParam = new UsageInfo
            {
                SentTraffic = Tunnel.ReceivedByteCount - _syncSentTraffic, // Intentionally Reversed: sending to tunnel means receiving form client,
                ReceivedTraffic = Tunnel.SentByteCount - _syncReceivedTraffic // Intentionally Reversed: receiving from tunnel means sending for client
            };

            var usedTraffic = usageParam.ReceivedTraffic + usageParam.SentTraffic;
            var shouldSync = closeSession || (force && usedTraffic > 0) || usedTraffic >= _syncCacheSize;
            if (!shouldSync)
                return;

            // reset usage and sync time; no matter it is successful or not to prevent frequent call
            _syncSentTraffic += usageParam.SentTraffic;
            _syncReceivedTraffic += usageParam.ReceivedTraffic;
            _isSyncing = true;
        }

        try
        {
            SessionResponse = closeSession
                ? await _accessServer.Session_Close(SessionId, usageParam)
                : await _accessServer.Session_AddUsage(SessionId, usageParam);

            // dispose for any error
            if (SessionResponse.ErrorCode != SessionErrorCode.Ok)
                await DisposeAsync(false, false);
        }
        catch (ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            SessionResponse.ErrorCode = SessionErrorCode.AccessError;
            SessionResponse.ErrorMessage = "Session Not Found.";
            await DisposeAsync(false, false);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(GeneralEventId.AccessServer, ex,
                "Could not report usage to the access-server.");
        }
        finally
        {
            lock (_syncLock)
                _isSyncing = false;
        }
    }

    public void LogTrack(string protocol, IPEndPoint? localEndPoint, IPEndPoint? destinationEndPoint,
        bool isNewLocal, bool isNewRemote, string? failReason)
    {
        if (!_trackingOptions.IsEnabled())
            return;

        if (_trackingOptions is { TrackDestinationIp: false, TrackDestinationPort: false } && !isNewLocal && failReason == null)
            return;

        if (!_trackingOptions.TrackTcp && protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase) ||
            !_trackingOptions.TrackUdp && protocol.Equals("udp", StringComparison.OrdinalIgnoreCase) ||
            !_trackingOptions.TrackUdp && protocol.Equals("icmp", StringComparison.OrdinalIgnoreCase))
            return;

        var mode = (isNewLocal ? "L" : "") + ((isNewRemote ? "R" : ""));
        var localPortStr = "-";
        var destinationIpStr = "-";
        var destinationPortStr = "-";
        var netScanCount = "-";
        failReason ??= "Ok";

        if (localEndPoint != null)
            localPortStr = _trackingOptions.TrackLocalPort ? localEndPoint.Port.ToString() : "*";

        if (destinationEndPoint != null)
        {
            destinationIpStr = _trackingOptions.TrackDestinationIp ? Util.RedactIpAddress(destinationEndPoint.Address) : "*";
            destinationPortStr = _trackingOptions.TrackDestinationPort ? destinationEndPoint.Port.ToString() : "*";
            netScanCount = NetScanDetector?.GetBurstCount(destinationEndPoint).ToString() ?? "*";
        }

        VhLogger.Instance.LogInformation(GeneralEventId.Track,
            "{Proto,-4}\tSessionId {SessionId}\t{Mode,-2}\tTcpCount {TcpCount,4}\tUdpCount {UdpCount,4}\tTcpWait {TcpConnectWaitCount,3}\tNetScan {NetScan,3}\t" +
            "SrcPort {SrcPort,-5}\tDstIp {DstIp,-15}\tDstPort {DstPort,-5}\t{Success,-10}",
            protocol, SessionId, mode,
            TcpChannelCount, _proxyManager.UdpClientCount, _tcpConnectWaitCount, netScanCount,
            localPortStr, destinationIpStr, destinationPortStr, failReason);
    }

    public async Task ProcessTcpChannelRequest(TcpClientStream tcpClientStream, TcpProxyChannelRequest request,
        CancellationToken cancellationToken)
    {
        var isRequestedEpException = false;
        var isTcpConnectIncreased = false;

        TcpClient? tcpClient2 = null;
        TcpClientStream? tcpClientStream2 = null;
        TcpProxyChannel? tcpProxyChannel = null;
        try
        {
            // connect to requested site
            VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel,
                $"Connecting to the requested endpoint. RequestedEP: {VhLogger.Format(request.DestinationEndPoint)}");

            // Apply limitation
            VerifyTcpChannelRequest(tcpClientStream, request);

            // prepare client
            Interlocked.Increment(ref _tcpConnectWaitCount);
            isTcpConnectIncreased = true;

            tcpClient2 = _socketFactory.CreateTcpClient(request.DestinationEndPoint.AddressFamily);
            _socketFactory.SetKeepAlive(tcpClient2.Client, true);

            //tracking
            LogTrack(ProtocolType.Tcp.ToString(), (IPEndPoint)tcpClient2.Client.LocalEndPoint, request.DestinationEndPoint,
                true, true, null);

            // connect to requested destination
            isRequestedEpException = true;
            await Util.RunTask(
                tcpClient2.ConnectAsync(request.DestinationEndPoint.Address, request.DestinationEndPoint.Port),
                _tcpConnectTimeout, cancellationToken);
            isRequestedEpException = false;

            // send response
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, this.SessionResponse, cancellationToken);

            // Dispose ssl stream and replace it with a Head-Cryptor
            await tcpClientStream.Stream.DisposeAsync();
            tcpClientStream.Stream = StreamHeadCryptor.Create(tcpClientStream.TcpClient.GetStream(),
                request.CipherKey, null, request.CipherLength);

            // add the connection
            VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel,
                $"Adding a {nameof(TcpProxyChannel)}. SessionId: {VhLogger.FormatSessionId(SessionId)}, CipherLength: {request.CipherLength}");

            tcpClientStream2 = new TcpClientStream(tcpClient2, tcpClient2.GetStream());
            tcpProxyChannel = new TcpProxyChannel(tcpClientStream2, tcpClientStream, _tcpTimeout, _tcpBufferSize, _tcpBufferSize);

            Tunnel.AddChannel(tcpProxyChannel);
        }
        catch (Exception ex)
        {
            _netScanExceptionReporter.Dispose();
            _maxTcpChannelExceptionReporter.Dispose();
            _maxTcpConnectWaitExceptionReporter.Dispose();
            tcpClient2?.Dispose();
            tcpClientStream2?.Dispose();
            tcpProxyChannel?.Dispose();

            if (isRequestedEpException)
                throw new ServerSessionException(tcpClientStream.IpEndPointPair.RemoteEndPoint,
                    this, SessionErrorCode.GeneralError, ex.Message);

            throw;
        }
        finally
        {
            if (isTcpConnectIncreased)
                Interlocked.Decrement(ref _tcpConnectWaitCount);
        }
    }

    private void VerifyTcpChannelRequest(TcpClientStream tcpClientStream, TcpProxyChannelRequest request)
    {
        lock (_verifyRequestLock)
        {
            // NetScan limit
            VerifyNetScan(ProtocolType.Tcp, request.DestinationEndPoint);

            // Channel Count limit
            if (TcpChannelCount >= _maxTcpChannelCount)
            {
                LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "MaxTcp");
                _maxTcpChannelExceptionReporter.Raised();
                throw new MaxTcpChannelException(tcpClientStream.RemoteEndPoint, this);
            }

            // Check tcp wait limit
            if (TcpConnectWaitCount >= _maxTcpConnectWaitCount)
            {
                LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, false, true, "MaxTcpWait");
                _maxTcpConnectWaitExceptionReporter.Raised();
                throw new MaxTcpConnectWaitException(tcpClientStream.RemoteEndPoint, this);
            }
        }
    }

    private void VerifyNetScan(ProtocolType protocol, IPEndPoint remoteEndPoint)
    {
        if (NetScanDetector == null || NetScanDetector.Verify(remoteEndPoint)) return;

        LogTrack(protocol.ToString(), null, remoteEndPoint, false, true, "NetScan");
        _netScanExceptionReporter.Raised();
        throw new NetScanException(remoteEndPoint, this);
    }

    private class SessionProxyManager : ProxyManager
    {
        private readonly Session _session;
        protected override bool IsPingSupported => true;

        public SessionProxyManager(Session session, ISocketFactory socketFactory, ProxyManagerOptions options)
            : base(socketFactory, options)
        {
            _session = session;
        }

        public override Task OnPacketReceived(IPPacket ipPacket)
        {
            if (VhLogger.IsDiagnoseMode)
                PacketUtil.LogPacket(ipPacket, "Delegating packet to client via proxy.");

            return _session.Tunnel.SendPacket(ipPacket);
        }

        public override void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
            _session.LogTrack(protocolType.ToString(), localEndPoint, remoteEndPoint, isNewLocalEndPoint, isNewRemoteEndPoint, null);
        }

        public override void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint)
        {
            _session.VerifyNetScan(protocolType, remoteEndPoint);
        }
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

        Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
        Tunnel.Dispose();
        _proxyManager.Dispose();

        if (sync)
            await Sync(true, byUser);

        // if there is no reason it is temporary
        var reason = "Cleanup";
        if (SessionResponse.ErrorCode != SessionErrorCode.Ok)
            reason = byUser ? "User" : "Access";

        // Report removing session
        VhLogger.Instance.LogInformation(GeneralEventId.SessionTrack,
            "SessionId: {SessionId-5}\t{Mode,-5}\tActor: {Actor,-7}\tSuppressBy: {SuppressedBy,-8}\tErrorCode: {ErrorCode,-20}\tMessage: {message}",
            SessionId, "Close", reason, SessionResponse.SuppressedBy, SessionResponse.ErrorCode, SessionResponse.ErrorMessage ?? "None");
    }
}
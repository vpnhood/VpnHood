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
    private bool _isSyncing;
    private long _syncReceivedTraffic;
    private long _syncSentTraffic;
    private readonly TrackingOptions _trackingOptions;
    private readonly EventReporter _netScanExceptionReporter = new(VhLogger.Instance, "NetScan protector does not allow this request.", GeneralEventId.NetProtect);
    private readonly EventReporter _maxTcpChannelExceptionReporter = new(VhLogger.Instance, "Maximum TcpChannel has been reached.", GeneralEventId.NetProtect);
    private readonly EventReporter _maxTcpConnectWaitExceptionReporter = new(VhLogger.Instance, "Maximum TcpConnectWait has been reached.", GeneralEventId.NetProtect);
    private int _tcpConnectWaitCount;

    public SessionOptions Options { get; }
    public Tunnel Tunnel { get; }
    public uint SessionId { get; }
    public byte[] SessionKey { get; }
    public SessionResponseBase SessionResponseBase { get; private set; }
    public UdpChannel? UdpChannel { get; private set; }
    public bool IsDisposed { get; private set; }
    public NetScanDetector? NetScanDetector { get; }
    public JobSection JobSection { get; }
    public HelloRequest? HelloRequest { get; }
    public int TcpConnectWaitCount => _tcpConnectWaitCount;
    public int TcpChannelCount => Tunnel.StreamChannelCount + (UseUdpChannel ? 0 : Tunnel.DatagramChannels.Length);
    public int UdpConnectionCount => _proxyManager.UdpConnectionCount + (UseUdpChannel ? 1 : 0);
    public DateTime LastActivityTime => Tunnel.LastActivityTime;

    internal Session(IAccessServer accessServer, SessionResponse sessionResponse, SocketFactory socketFactory,
        IPEndPoint localEndPoint, SessionOptions options, TrackingOptions trackingOptions, HelloRequest? helloRequest)
    {
        _accessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _proxyManager = new SessionProxyManager(this, socketFactory, options);
        _localEndPoint = localEndPoint;
        _trackingOptions = trackingOptions;
        Options = options;
        HelloRequest = helloRequest;
        SessionResponseBase = new SessionResponseBase(sessionResponse);
        SessionId = sessionResponse.SessionId;
        SessionKey = sessionResponse.SessionKey ?? throw new InvalidOperationException($"{nameof(sessionResponse)} does not have {nameof(sessionResponse.SessionKey)}!");
        JobSection = new JobSection(options.SyncInterval);

        var tunnelOptions = new TunnelOptions();
        if (options.MaxDatagramChannelCount is > 0) tunnelOptions.MaxDatagramChannelCount = options.MaxDatagramChannelCount.Value;
        Tunnel = new Tunnel(tunnelOptions);
        Tunnel.OnPacketReceived += Tunnel_OnPacketReceived;

        if (options.NetScanLimit != null && options.NetScanTimeout != null)
            NetScanDetector = new NetScanDetector(options.NetScanLimit.Value, options.NetScanTimeout.Value);

        if (trackingOptions.IsEnabled())
            _proxyManager.OnNewEndPointEstablished += OnNewEndPointEstablished;
        
        if (NetScanDetector!=null)
            _proxyManager.OnNewRemoteEndPoint += OnNewRemoteEndPoint;

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
        if (!IsDisposed)
            _proxyManager.SendPacket(e.IpPackets);
    }

    public Task Sync() => Sync(true, false);

    private async Task Sync(bool force, bool closeSession)
    {
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
            var shouldSync = closeSession || (force && usedTraffic > 0) || usedTraffic >= Options.SyncCacheSize;
            if (!shouldSync)
                return;

            // reset usage and sync time; no matter it is successful or not to prevent frequent call
            _syncSentTraffic += usageParam.SentTraffic;
            _syncReceivedTraffic += usageParam.ReceivedTraffic;
            _isSyncing = true;
        }

        try
        {
            SessionResponseBase = closeSession
                ? await _accessServer.Session_Close(SessionId, usageParam)
                : await _accessServer.Session_AddUsage(SessionId, usageParam);

            // dispose for any error
            if (SessionResponseBase.ErrorCode != SessionErrorCode.Ok)
            {
                VhLogger.Instance.LogInformation(GeneralEventId.Session,
                    $"The session has been closed by the access server. ErrorCode: {SessionResponseBase.ErrorCode}");
                await DisposeAsync(false, false);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "The session does not exist in the access server.");
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

    public void Dispose()
    {
        DisposeAsync(false).GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(false);
    }

    public async ValueTask DisposeAsync(bool closeSessionInAccessServer, bool log = true)
    {
        if (IsDisposed) return;
        IsDisposed = true;

        Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
        Tunnel.Dispose();
        _proxyManager.Dispose();

        await Sync(true, closeSessionInAccessServer);

        // Report removing session
        if (log)
            VhLogger.Instance.LogInformation(GeneralEventId.Session, "The session has been {State} closed. SessionId: {SessionId}.",
                closeSessionInAccessServer ? "permanently" : "temporary", SessionId);
    }

    private void OnNewRemoteEndPoint(object sender, EndPointEventArgs e)
    {
        if (NetScanDetector!=null && !NetScanDetector.Verify(e.RemoteEndPoint))
        {
            LogTrack(e.ProtocolType.ToString(), null, e.RemoteEndPoint, true, true, "NetScan");
            _netScanExceptionReporter.Raised();
            throw new NetScanException(_localEndPoint, this);
        }
    }

    private void OnNewEndPointEstablished(object sender, EndPointEventPairArgs e)
    {
        LogTrack(e.ProtocolType.ToString(), e.LocalEndPoint, e.RemoteEndPoint,
            e.IsNewLocalEndPoint, e.IsNewRemoteEndPoint, null);
    }

    public void LogTrack(string protocol, IPEndPoint? localEndPoint, IPEndPoint destinationEndPoint, bool isNewLocal, bool isNewRemote, string? failReason)
    {
        if (!_trackingOptions.IsEnabled())
            return;

        var mode = (isNewLocal ? "L" : "") + ((isNewRemote ? "R" : ""));
        var localPortStr = _trackingOptions.TrackLocalPort ? localEndPoint?.Port.ToString() ?? "-" : "*";
        //todo
        //var destinationIpStr = _trackingOptions.TrackDestinationIp ? Util.RedactIpAddress(destinationEndPoint.Address) : "*";
        var destinationIpStr = _trackingOptions.TrackDestinationIp ? destinationEndPoint.Address.ToString() : "*";
        var destinationPortStr = _trackingOptions.TrackDestinationPort ? destinationEndPoint.Port.ToString() : "*";
        var netScanCount = NetScanDetector?.GetBurstCount(destinationEndPoint).ToString() ?? "*";
        failReason ??= "Ok";

        var log =
            "{Proto,-4}; SessionId {SessionId}; {Mode,-2}; TcpCount {TcpCount,4}; UdpCount {UdpCount,4}; TcpWait {TcpConnectWaitCount,3}; NetScan {NetScan,3}; " +
            "SrcPort {SrcPort,-5}; DstIp {DstIp,-15}; DstPort {DstPort,-5}; {Success,-10}";

        log = log.Replace("; ", "\t");

        VhLogger.Instance.LogInformation(GeneralEventId.Track,
            log,
            protocol, mode, SessionId,
            TcpChannelCount, _proxyManager.UdpClientCount, _tcpConnectWaitCount, netScanCount,
            localPortStr, destinationIpStr, destinationPortStr, failReason);
    }

    private class SessionProxyManager : ProxyManager
    {
        private readonly Session _session;
        protected override bool IsPingSupported => true;

        public SessionProxyManager(Session session, ISocketFactory socketFactory, SessionOptions sessionOptions)
            : base(socketFactory)
        {
            _session = session;
            UdpTimeout = sessionOptions.UdpTimeout;
            TcpTimeout = sessionOptions.TcpTimeout;
            IcmpTimeout = sessionOptions.IcmpTimeout;
            UdpClientMaxCount = sessionOptions.MaxUdpPortCount is null or 0 ? int.MaxValue : sessionOptions.MaxUdpPortCount.Value;
        }

        protected override Task OnPacketReceived(IPPacket ipPacket)
        {
            return _session.Tunnel.SendPacket(ipPacket);
        }
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
            LogTrack(ProtocolType.Tcp.ToString(), (IPEndPoint)tcpClient2.Client.LocalEndPoint,
                request.DestinationEndPoint,
                true, true, null);

            // connect to requested destination
            isRequestedEpException = true;
            await Util.RunTask(
                tcpClient2.ConnectAsync(request.DestinationEndPoint.Address, request.DestinationEndPoint.Port),
                Options.TcpConnectTimeout, cancellationToken);
            isRequestedEpException = false;

            // send response
            await StreamUtil.WriteJsonAsync(tcpClientStream.Stream, this.SessionResponseBase, cancellationToken);

            // Dispose ssl stream and replace it with a Head-Cryptor
            await tcpClientStream.Stream.DisposeAsync();
            tcpClientStream.Stream = StreamHeadCryptor.Create(tcpClientStream.TcpClient.GetStream(),
                request.CipherKey, null, request.CipherLength);

            // add the connection
            VhLogger.Instance.LogTrace(GeneralEventId.StreamChannel,
                $"Adding a {nameof(TcpProxyChannel)}. SessionId: {VhLogger.FormatSessionId(SessionId)}, CipherLength: {request.CipherLength}");

            tcpClientStream2 = new TcpClientStream(tcpClient2, tcpClient2.GetStream());
            tcpProxyChannel = new TcpProxyChannel(tcpClientStream2, tcpClientStream, Options.TcpTimeout, Options.TcpBufferSize, Options.TcpBufferSize);

            Tunnel.AddChannel(tcpProxyChannel);
        }
        catch (Exception ex)
        {
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
            if (NetScanDetector != null && !NetScanDetector.Verify(request.DestinationEndPoint))
            {
                LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, true, true, "NetScan");
                _netScanExceptionReporter.Raised();
                throw new NetScanException(request.DestinationEndPoint, this);
            }

            // Channel Count limit
            if (TcpChannelCount >= Options.MaxTcpChannelCount)
            {
                LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, true, true, "MaxTcp");
                _maxTcpChannelExceptionReporter.Raised();
                throw new MaxTcpChannelException(tcpClientStream.RemoteEndPoint, this);
            }

            // Check tcp wait limit
            if (TcpConnectWaitCount >= Options.MaxTcpConnectWaitCount)
            {
                LogTrack(ProtocolType.Tcp.ToString(), null, request.DestinationEndPoint, true, true, "MaxTcpWait");
                _maxTcpConnectWaitExceptionReporter.Raised();
                throw new MaxTcpConnectWaitException(tcpClientStream.RemoteEndPoint, this);
            }
        }
    }
}
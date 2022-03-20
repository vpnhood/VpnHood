using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Server
{
    public class Session : IDisposable, IAsyncDisposable
    {
        private readonly IAccessServer _accessServer;

        private readonly SessionProxyManager _proxyManager;
        private readonly SocketFactory _socketFactory;
        private readonly long _syncCacheSize;
        private readonly TimeSpan _syncInterval;
        private readonly IPEndPoint _hostEndPoint;
        private readonly object _syncLock = new();
        private bool _isSyncing;
        private long _syncReceivedTraffic;
        private long _syncSentTraffic;
        private readonly Timer _cleanupTimer;
        private DateTime _lastSyncedTime = DateTime.Now;

        internal Session(IAccessServer accessServer, SessionResponse sessionResponse, SocketFactory socketFactory,
            IPEndPoint hostEndPoint, SessionOptions options, TrackingOptions trackingOptions)
        {
            _accessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
            _proxyManager = new SessionProxyManager(this, options, trackingOptions);
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _syncCacheSize = options.SyncCacheSize;
            _syncInterval = options.SyncInterval;
            _hostEndPoint = hostEndPoint;
            _cleanupTimer = new Timer(Cleanup, null, options.IcmpTimeout, options.IcmpTimeout);
            SessionResponse = new ResponseBase(sessionResponse);
            SessionId = sessionResponse.SessionId;
            SessionKey = sessionResponse.SessionKey ?? throw new InvalidOperationException($"{nameof(sessionResponse)} does not have {nameof(sessionResponse.SessionKey)}!");

            var tunnelOptions = new TunnelOptions();
            if (options.MaxDatagramChannelCount > 0) tunnelOptions.MaxDatagramChannelCount = options.MaxDatagramChannelCount;
            Tunnel = new Tunnel(tunnelOptions);
            Tunnel.OnPacketReceived += Tunnel_OnPacketReceived;
            Tunnel.OnTrafficChanged += Tunnel_OnTrafficChanged;
        }

        private void Cleanup(object state)
        {
            _proxyManager.Cleanup();
            Tunnel.Cleanup();

            if (DateTime.Now - _lastSyncedTime > _syncInterval)
                _ = Sync();
        }

        public Tunnel Tunnel { get; }
        public uint SessionId { get; }
        public byte[] SessionKey { get; }
        public ResponseBase SessionResponse { get; private set; }
        public UdpChannel? UdpChannel { get; private set; }
        public bool IsDisposed { get; private set; }

        public int TcpConnectionCount =>
            Tunnel.StreamChannelCount + (UseUdpChannel ? 0 : Tunnel.DatagramChannels.Length);

        public int UdpConnectionCount => _proxyManager.UdpConnectionCount + (UseUdpChannel ? 1 : 0);
        public DateTime LastActivityTime => Tunnel.LastActivityTime;

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
                    UdpChannel = new UdpChannel(false, _socketFactory.CreateUdpClient(_hostEndPoint.AddressFamily), SessionId, aes.Key);
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

        private void Tunnel_OnTrafficChanged(object sender, EventArgs e)
        {
            _ = Sync(false, false);
        }

        public Task Sync() => Sync(true, false);

        private async Task Sync(bool force, bool closeSession)
        {
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

                var usageTraffic = usageParam.ReceivedTraffic + usageParam.SentTraffic;
                var shouldSync = closeSession || (force && usageTraffic > 0) || usageTraffic >= _syncCacheSize;
                if (!shouldSync)
                    return;

                _isSyncing = true;
            }

            try
            {
                SessionResponse = await _accessServer.Session_AddUsage(SessionId, closeSession, usageParam);
                lock (_syncLock)
                {
                    _syncSentTraffic += usageParam.SentTraffic;
                    _syncReceivedTraffic += usageParam.ReceivedTraffic;
                    _lastSyncedTime = DateTime.Now;
                }

                // dispose for any error
                if (SessionResponse.ErrorCode != SessionErrorCode.Ok)
                {
                    VhLogger.Instance.LogInformation($"Session closed by access server. ErrorCode: {SessionResponse.ErrorCode}");
                    await DisposeAsync();
                }
            }
            finally
            {
                lock (_syncLock)
                    _isSyncing = false;
            }
        }

        public void Dispose()
        {
            Dispose(false);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsync(false);
        }

        public void Dispose(bool closeSessionInAccessServer)
        {
            if (IsDisposed) return;
            IsDisposed = true;

            Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
            Tunnel.OnTrafficChanged -= Tunnel_OnTrafficChanged;
            Tunnel.Dispose();
            _proxyManager.Dispose();

            _ = Sync(true, closeSessionInAccessServer);
        }

        public async ValueTask DisposeAsync(bool closeSessionInAccessServer)
        {
            if (IsDisposed) return;
            IsDisposed = true;

            Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
            Tunnel.OnTrafficChanged -= Tunnel_OnTrafficChanged;
            Tunnel.Dispose();
            _proxyManager.Dispose();

            await _cleanupTimer.DisposeAsync();
            await Sync(true, closeSessionInAccessServer);
        }

        private class SessionProxyManager : ProxyManager
        {
            private readonly Session _session;
            private readonly TrackingOptions _trackingOptions;
            protected override bool IsPingSupported => true;

            public SessionProxyManager(Session session, SessionOptions sessionOptions, TrackingOptions trackingOptions)
            {
                _session = session;
                _trackingOptions = trackingOptions;
                UdpTimeout = sessionOptions.UdpTimeout;
                TcpTimeout = sessionOptions.TcpTimeout;
                MaxUdpPortCount = sessionOptions.MaxUdpPortCount;
            }

            protected override UdpClient CreateUdpClient(AddressFamily addressFamily)
            {
                var udpClient = _session._socketFactory.CreateUdpClient(addressFamily);

                //tracking
                if (_trackingOptions.LogLocalPort)
                {
                    var log = $"Udp | SessionId: {_session.SessionId}, Port: {((IPEndPoint)udpClient.Client.LocalEndPoint).Port}";
                    VhLogger.Instance.LogInformation(GeneralEventId.Track, log);
                }

                return udpClient;
            }

            protected override Task OnPacketReceived(IPPacket ipPacket)
            {
                return _session.Tunnel.SendPacket(ipPacket);
            }
        }
    }
}
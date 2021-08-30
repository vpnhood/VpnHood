using PacketDotNet;
using System;
using VpnHood.Tunneling;
using System.Security.Cryptography;
using System.Linq;
using System.Net.NetworkInformation;
using VpnHood.Tunneling.Factory;
using VpnHood.Common.Messaging;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public class Session : IDisposable
    {
        private class SessionProxyManager : ProxyManager
        {
            private readonly Session _session;
            public SessionProxyManager(Session session) => _session = session;
            protected override Ping CreatePing() => new();
            protected override System.Net.Sockets.UdpClient CreateUdpClient() => _session._socketFactory.CreateUdpClient();
            protected override void SendReceivedPacket(IPPacket ipPacket) => _session.Tunnel.SendPacket(ipPacket);
        }

        private readonly SessionProxyManager _sessionProxyManager;
        private readonly SocketFactory _socketFactory;
        private long _syncSentTraffic = 0;
        private long _syncReceivedTraffic = 0;
        private bool _isSyncing = false;
        private readonly object _syncLock = new();
        private readonly long _syncCacheSize;
        private readonly IAccessServer _accessServer;

        public Tunnel Tunnel { get; }
        public uint SessionId { get; }
        public byte[] SessionKey { get; }
        public ResponseBase SessionResponse { get; private set; }
        public UdpChannel? UdpChannel { get; private set; } //todo use global udp listener
        public bool IsDisposed { get; private set; }
        public int TcpConnectionCount => Tunnel.StreamChannelCount + (UseUdpChannel ? 0 : Tunnel.DatagramChannels.Count());
        public int UdpConnectionCount => _sessionProxyManager.UdpConnectionCount + (UseUdpChannel ? 1 : 0);
        public DateTime LastActivityTime => Tunnel.LastActivityTime;

        internal Session(IAccessServer accessServer, SessionResponse sessionResponse, SocketFactory socketFactory,
            int maxDatagramChannelCount, long syncCacheSize)
        {
            _accessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
            _sessionProxyManager = new SessionProxyManager(this);
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _syncCacheSize = syncCacheSize;
            SessionResponse = new ResponseBase(sessionResponse);
            SessionId = sessionResponse.SessionId;
            SessionKey = sessionResponse.SessionKey ?? throw new InvalidOperationException($"{nameof(sessionResponse)} does not have {nameof(sessionResponse.SessionKey)}!");
            Tunnel = new Tunnel
            {
                MaxDatagramChannelCount = maxDatagramChannelCount
            };
            Tunnel.OnPacketReceived += Tunnel_OnPacketReceived;
            Tunnel.OnTrafficChanged += Tunnel_OnTrafficChanged;
        }

        private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
        {
            if (!IsDisposed)
                _sessionProxyManager.SendPacket(e.IpPackets);
        }

        private void Tunnel_OnTrafficChanged(object sender, EventArgs e)
        {
            _ = Sync();
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
                    UdpChannel = new UdpChannel(false, _socketFactory.CreateUdpClient(), SessionId, aes.Key);
                    Tunnel.AddChannel(UdpChannel);
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

        private async Task Sync(bool closeSession = false)
        {
            UsageInfo usageParam;
            lock (_syncLock)
            {
                if (_isSyncing)
                    return;

                usageParam = new UsageInfo
                {
                    SentTraffic = Tunnel.ReceivedByteCount - _syncSentTraffic, // Intentionally Reversed: sending to tunnel means receiving form client,
                    ReceivedTraffic = Tunnel.SentByteCount - _syncReceivedTraffic, // Intentionally Reversed: receiving from tunnel means sending for client
                };
                if (!closeSession && (usageParam.SentTraffic + usageParam.ReceivedTraffic) < _syncCacheSize)
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
                }

                // dispose for any error
                if (SessionResponse.ErrorCode != SessionErrorCode.Ok)
                    Dispose();
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="closeSession">notify access server to close session</param>
        public void Dispose(bool closeSessionInAccessSever)
        {
            if (!IsDisposed)
            {
                Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
                Tunnel.OnTrafficChanged -= Tunnel_OnTrafficChanged;
                Tunnel.Dispose();

                _sessionProxyManager.Dispose();
                _ = Sync(closeSessionInAccessSever);

                IsDisposed = true;
            }
        }

    }
}


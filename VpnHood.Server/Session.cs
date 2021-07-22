using PacketDotNet;
using System;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messages;
using System.Security.Cryptography;
using System.Linq;
using System.Net.NetworkInformation;
using VpnHood.Tunneling.Factory;
using System.Net;

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
        private long _lastTunnelSendByteCount = 0;
        private long _lastTunnelReceivedByteCount = 0;

        public IPEndPoint ServerEndPoint { get; }
        public int Timeout { get; }
        public AccessController AccessController { get; }
        public Tunnel Tunnel { get; }
        public Guid ClientId => ClientIdentity.ClientId;
        public ClientIdentity ClientIdentity { get; }
        public int SessionId { get; }
        public byte[] SessionKey { get; }
        public Guid? SuppressedToClientId { get; internal set; }
        public Guid? SuppressedByClientId { get; internal set; }
        public DateTime CreatedTime { get; } = DateTime.Now;
        public UdpChannel UdpChannel { get; private set; }
        public bool IsDisposed { get; private set; }

        internal Session(ClientIdentity clientIdentity, IPEndPoint serverEndPoint, AccessController accessController, SocketFactory socketFactory, 
            int timeout, int maxDatagramChannelCount)
        {
            if (accessController is null) throw new ArgumentNullException(nameof(accessController));
            _sessionProxyManager = new SessionProxyManager(this);

            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            ClientIdentity = clientIdentity; // note: it is different than accessController.ClientIdentity
            AccessController = accessController;
            ServerEndPoint = serverEndPoint;
            SessionId = new Random().Next();
            Timeout = timeout;
            Tunnel = new Tunnel
            {
                MaxDatagramChannelCount = maxDatagramChannelCount
            };
            Tunnel.OnPacketReceived += Tunnel_OnPacketReceived;
            Tunnel.OnTrafficChanged += Tunnel_OnTrafficChanged;

            // create SessionKey
            using var aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();
            SessionKey = aes.Key;
        }

        public SuppressType SuppressedTo
        {
            get
            {
                if (SuppressedToClientId == null) return SuppressType.None;
                else if (SuppressedToClientId.Value == ClientId) return SuppressType.YourSelf;
                else return SuppressType.Other;
            }
        }

        public SuppressType SuppressedBy
        {
            get
            {
                if (SuppressedByClientId == null) return SuppressType.None;
                else if (SuppressedByClientId.Value == ClientId) return SuppressType.YourSelf;
                else return SuppressType.Other;
            }
        }

        private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
            => _sessionProxyManager.SendPacket(e.IpPackets);

        private void Tunnel_OnTrafficChanged(object sender, EventArgs e) 
            => UpdateStatus();

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

        internal void UpdateStatus()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(Session));

            var tunnelSentByteCount = Tunnel.ReceivedByteCount; // Intentionally Reversed: sending to tunnel means receiving form client
            var tunnelReceivedByteCount = Tunnel.SentByteCount; // Intentionally Reversed: receiving from tunnel means sending for client
            if (tunnelSentByteCount != _lastTunnelSendByteCount || tunnelReceivedByteCount != _lastTunnelReceivedByteCount)
            {
                _ = AccessController.AddUsage(ClientIdentity, tunnelSentByteCount - _lastTunnelSendByteCount, tunnelReceivedByteCount - _lastTunnelReceivedByteCount);
                _lastTunnelSendByteCount = tunnelSentByteCount;
                _lastTunnelReceivedByteCount = tunnelReceivedByteCount;
            }

            // update AccessController status
            AccessController.UpdateStatusCode();

            // Dispose if access denied or sesstion has been time out
            if (AccessController.Access.StatusCode != AccessStatusCode.Ok ||
                (DateTime.Now - Tunnel.LastActivityTime).TotalSeconds > Timeout)
                Dispose();
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            _ = AccessController.Sync(ClientIdentity);
            Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
            Tunnel.OnTrafficChanged -= Tunnel_OnTrafficChanged;
            Tunnel.Dispose();

            _sessionProxyManager.Dispose();
        }
    }
}


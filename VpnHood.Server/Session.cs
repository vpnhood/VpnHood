using VpnHood.Server.Factory;
using PacketDotNet;
using System;
using System.Net;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messages;
using System.Security.Cryptography;
using System.Linq;
using System.IO;

namespace VpnHood.Server
{
    public class Session : IDisposable
    {
        private readonly Nat _nat;
        private readonly UdpClientFactory _udpClientFactory;
        private readonly PingProxy _pingProxy;
        private long _lastTunnelSendByteCount = 0;
        private long _lastTunnelReceivedByteCount = 0;
        private readonly IPAddress[] _blockList = new[] {
            IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play)/SSDP (Simple Service Discovery Protocol)
        };

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

        internal Session(ClientIdentity clientIdentity, AccessController accessController, UdpClientFactory udpClientFactory, int timeout)
        {
            if (accessController is null) throw new ArgumentNullException(nameof(accessController));

            _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
            _nat = new Nat(false);
            _nat.OnNatItemRemoved += Nat_OnNatItemRemoved;
            _pingProxy = new PingProxy();
            _pingProxy.OnPacketReceived += PingProxy_OnPacketReceived;
            AccessController = accessController;
            ClientIdentity = clientIdentity;
            SessionId = new Random().Next();
            Timeout = timeout;
            Tunnel = new Tunnel();
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

        private void PingProxy_OnPacketReceived(object sender, PacketReceivedEventArgs e)
        {
            Tunnel.SendPacket(e.IpPacket);
        }
        private void UdpProxy_OnPacketReceived(object sender, PacketReceivedEventArgs e)
        {
            Tunnel.SendPacket(e.IpPacket);
        }

        private void Nat_OnNatItemRemoved(object sender, NatEventArgs e)
        {
            if (e.NatItem.Tag is UdpProxy udpProxy)
            {
                udpProxy.OnPacketReceived -= UdpProxy_OnPacketReceived;
                udpProxy.Dispose();
            }
        }

        private void Tunnel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
        {
            foreach (var ipPacket in e.IpPackets)
                ProcessPacket(ipPacket);
        }

        private void ProcessPacket(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Version != IPVersion.IPv4) throw new Exception($"{ipPacket.Version} packets is not supported!");

            if (ipPacket.Protocol == ProtocolType.Udp)
                ProcessUdpPacket(ipPacket);

            else if (ipPacket.Protocol == ProtocolType.Icmp)
                ProcessIcmpPacket(ipPacket);

            else if (ipPacket.Protocol == ProtocolType.Tcp)
                throw new Exception("Tcp Packet should not be sent through this channel! Use TcpProxy.");

            else
                throw new Exception($"{ipPacket.Protocol} is not supported yet!");
        }

        private void ProcessUdpPacket(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

            // drop blocke packets
            if (_blockList.Any(x => x.Equals(ipPacket.DestinationAddress)))
                return;

            // send packet via proxy
            var natItem = _nat.Get(ipPacket);
            if (natItem?.Tag is not UdpProxy udpProxy || udpProxy.IsDisposed)
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                udpProxy = new UdpProxy(_udpClientFactory, new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort));
                udpProxy.OnPacketReceived += UdpProxy_OnPacketReceived;
                natItem = _nat.Add(ipPacket, (ushort)udpProxy.LocalPort, true);
                natItem.Tag = udpProxy;
            }
            udpProxy.Send(ipPacket);
        }

        private void ProcessIcmpPacket(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            _pingProxy.Send(ipPacket);
        }

        private void Tunnel_OnTrafficChanged(object sender, EventArgs e)
        {
            UpdateStatus();
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
                    UdpChannel = new UdpChannel(false, _udpClientFactory.CreateListner(), SessionId, aes.Key);
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
                var _ = AccessController.AddUsage(tunnelSentByteCount - _lastTunnelSendByteCount, tunnelReceivedByteCount - _lastTunnelReceivedByteCount);
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

            var _ = AccessController.Sync();
            Tunnel.OnPacketReceived -= Tunnel_OnPacketReceived;
            Tunnel.OnTrafficChanged -= Tunnel_OnTrafficChanged;
            _pingProxy.OnPacketReceived -= PingProxy_OnPacketReceived;

            IsDisposed = true; // mark disposed here
            Tunnel.Dispose();
            _pingProxy.Dispose();
            _nat.Dispose();
        }
    }
}


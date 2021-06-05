using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using VpnHood.Logging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messages;
using System.Security.Cryptography;

namespace VpnHood.Server
{
    public class Session : IDisposable
    {
        private readonly Nat _nat;
        private readonly UdpClientFactory _udpClientFactory;
        private readonly PingProxy _pingProxy;
        private long _lastTunnelSendByteCount = 0;
        private long _lastTunnelReceivedByteCount = 0;

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
        public UdpChannel UdpChannel {get;}
        public bool IsDisposed { get; private set; }

        internal Session(ClientIdentity clientIdentity, AccessController accessController, UdpClientFactory udpClientFactory, int timeout)
        {
            if (accessController is null) throw new ArgumentNullException(nameof(accessController));

            _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
            _nat = new Nat(false);
            _nat.OnNatItemRemoved += Nat_OnNatItemRemoved;
            _pingProxy = new PingProxy();
            _pingProxy.OnPingCompleted += PingProxy_OnPingCompleted;
            AccessController = accessController;
            ClientIdentity = clientIdentity;
            SessionId = TunnelUtil.RandomInt();
            Timeout = timeout;
            Tunnel = new Tunnel();
            Tunnel.OnPacketArrival += Tunnel_OnPacketArrival;
            Tunnel.OnTrafficChanged += Tunnel_OnTrafficChanged;

            // create SessionKey
            using var aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();
            SessionKey = aes.Key;

            // Create the only one UdpChannel
            UdpChannel = new UdpChannel(false, _udpClientFactory.CreateListner(), SessionId, SessionKey);
            Tunnel.AddChannel(UdpChannel);
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

        private void PingProxy_OnPingCompleted(object sender, PingCompletedEventArgs e)
        {
            Tunnel.SendPacket(e.IpPacket);
        }
        private void Nat_OnNatItemRemoved(object sender, NatEventArgs e)
        {
            (e.NatItem.Tag as UdpClient)?.Dispose();
        }

        private void Tunnel_OnPacketArrival(object sender, ChannelPacketArrivalEventArgs e)
        {
            foreach (var ipPacket in e.IpPackets)
                if (ipPacket is IPv4Packet ipv4Packet)
                    ProcessPacket(ipv4Packet);
        }

        private void ProcessPacket(IPv4Packet ipPacket)
        {
            if (ipPacket.Protocol == PacketDotNet.ProtocolType.Udp)
                ProcessUdpPacket(ipPacket);

            else if (ipPacket.Protocol == PacketDotNet.ProtocolType.Icmp)
                ProcessIcmpPacket(ipPacket);

            else if (ipPacket.Protocol == PacketDotNet.ProtocolType.Tcp)
                throw new Exception("Tcp Packet should not be sent through this channel! Use TcpProxy.");

            else
                throw new Exception($"{ipPacket.Protocol} is not supported yet!");
        }

        private void ProcessUdpPacket(IPv4Packet ipPacket)
        {
            var natItem = _nat.Get(ipPacket);
            var udpClient = (UdpClient)natItem?.Tag;
            if (natItem == null)
            {
                udpClient = _udpClientFactory.CreateListner();
                udpClient.EnableBroadcast = true;
                natItem = _nat.Add(ipPacket, (ushort)((IPEndPoint)udpClient.Client.LocalEndPoint).Port);
                natItem.Tag = udpClient;
                var thread = new Thread(ReceiveUdpThread, TunnelUtil.SocketStackSize_Datagram);
                thread.Start(natItem);
            }

            var udpPacket = ipPacket.Extract<UdpPacket>();
            var dgram = udpPacket.PayloadData;
            if (dgram == null)
                return;

            var ipEndPoint = new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort);
            udpClient.DontFragment = (ipPacket.FragmentFlags & 0x2) != 0;
            udpPacket.UpdateUdpChecksum();
            try
            {
                var sentBytes = udpClient.Send(dgram, dgram.Length, ipEndPoint);
                if (sentBytes != dgram.Length)
                    VhLogger.Instance.LogWarning($"Couldn't send all udp bytes. Requested: {dgram.Length}, Sent: {sentBytes}");
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning($"Couldn't send a udp packet to {VhLogger.Format(ipEndPoint)}. Error: {ex.Message}");
            }
        }

        private void ReceiveUdpThread(object obj)
        {
            var natItem = (NatItem)obj;
            var udpClient = (UdpClient)natItem.Tag;
            var localEndPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;

            try
            {
                while (true)
                {
                    //receiving packet
                    var ipEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    var udpResult = udpClient.Receive(ref ipEndPoint);

                    // forward packet
                    var ipPacket = new IPv4Packet(ipEndPoint.Address, natItem.SourceAddress);
                    var udpPacket = new UdpPacket((ushort)ipEndPoint.Port, natItem.SourcePort)
                    {
                        PayloadData = udpResult
                    };
                    ipPacket.PayloadPacket = udpPacket;
                    udpPacket.UpdateUdpChecksum();
                    ipPacket.UpdateIPChecksum();
                    ipPacket.UpdateCalculatedValues();
                    Tunnel.SendPacket(ipPacket);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"{ex.Message}, LocalEp: {VhLogger.Format(localEndPoint)}");
            }
        }

        private void ProcessIcmpPacket(IPv4Packet ipPacket)
        {
            _pingProxy.Send(ipPacket);
        }

        private void Tunnel_OnTrafficChanged(object sender, EventArgs e)
        {
            UpdateStatus();
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
            Tunnel.OnPacketArrival -= Tunnel_OnPacketArrival;
            Tunnel.OnTrafficChanged -= Tunnel_OnTrafficChanged;
            _pingProxy.OnPingCompleted -= PingProxy_OnPingCompleted;

            IsDisposed = true; // mark disposed here
            Tunnel.Dispose();
            _pingProxy.Dispose();
            _nat.Dispose();
        }
    }
}


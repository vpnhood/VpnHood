using VpnHood.Loggers;
using VpnHood.Messages;
using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.ComponentModel.Design;
using System.Net.Mime;

namespace VpnHood.Server
{

    public class Session : IDisposable
    {
        private const int SESSION_Timeout = 15 * 60;
        private readonly Nat _nat;
        private readonly UdpClientFactory _udpClientFactory;
        private readonly PingProxy _pingProxy;
        private long _lastTunnelSendByteCount = 0;
        private long _lastTunnelReceivedByteCount = 0;
        private ILogger Logger => Loggers.Logger.Current;

        public AccessController AccessController { get; }
        public Tunnel Tunnel { get; }
        public Guid ClientId => ClientIdentity.ClientId;
        public ClientIdentity ClientIdentity { get; }
        public ulong SessionId { get; }
        public Guid? SuppressedToClientId { get; internal set; }
        public Guid? SuppressedByClientId { get; internal set; }
        public DateTime CreatedTime { get; } = DateTime.Now;
        public bool IsDisposed => DisposeTime != null;
        public DateTime? DisposeTime { get; private set; }

        internal Session(ClientIdentity clientIdentity, AccessController accessController, UdpClientFactory udpClientFactory)
        {
            if (accessController is null) throw new ArgumentNullException(nameof(accessController));

            _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
            _nat = new Nat(false);
            _nat.OnNatItemRemoved += Nat_OnNatItemRemoved;
            _pingProxy = new PingProxy();
            AccessController = accessController;
            ClientIdentity = clientIdentity;
            SessionId = Util.RandomLong();
            Tunnel = new Tunnel();

            Tunnel.OnPacketArrival += Tunnel_OnPacketArrival;
            _pingProxy.OnPingCompleted += PingProxy_OnPingCompleted;
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
            if (e.IpPacket.Protocol == PacketDotNet.ProtocolType.Udp)
                ProcessUdpPacket((IPv4Packet)e.IpPacket);

            else if (e.IpPacket.Protocol == PacketDotNet.ProtocolType.Icmp)
                ProcessIcmpPacket((IPv4Packet)e.IpPacket);

            else if (e.IpPacket.Protocol == PacketDotNet.ProtocolType.Tcp)
                throw new Exception("Tcp Packet should not be sent through this channel! Use TcpProxy.");

            else
                throw new Exception($"{e.IpPacket.Protocol} is not supported yet!");
        }

        private void ProcessUdpPacket(IPv4Packet ipPacket)
        {
            var natItem = _nat.Get(ipPacket);
            var udpClient = (UdpClient)natItem?.Tag;
            if (natItem == null)
            {
                udpClient = _udpClientFactory.CreateListner();
                natItem = _nat.Add(ipPacket, (ushort)((IPEndPoint)udpClient.Client.LocalEndPoint).Port);
                natItem.Tag = udpClient;
                var thread = new Thread(ReceiveUdpThread, Util.SocketStackSize_Datagram);
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
                    Logger.LogWarning($"Couldn't send all udp bytes. Requested: {dgram.Length}, Sent: {sentBytes}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Couldn't a udp packet to {ipEndPoint}. Error: {ex.Message}");
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
                Logger.LogError($"{ex.Message}, LocalEp: ${localEndPoint}.");
            }
        }

        private void ProcessIcmpPacket(IPv4Packet ipPacket)
        {
            _pingProxy.Send(ipPacket);
        }

        internal void UpdateStatus()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(Session));

            var tunnelSentByteCount = Tunnel.SentByteCount;
            var tunnelReceivedByteCount = Tunnel.ReceivedByteCount;
            if (tunnelSentByteCount != _lastTunnelSendByteCount || tunnelReceivedByteCount != _lastTunnelReceivedByteCount)
            {
                AccessController.AddUsage(tunnelSentByteCount - _lastTunnelSendByteCount, _lastTunnelReceivedByteCount -tunnelReceivedByteCount);
                _lastTunnelSendByteCount = tunnelSentByteCount;
                _lastTunnelReceivedByteCount = tunnelReceivedByteCount;
            }

            // update AccessController status
            AccessController.UpdateStatusCode();

            // Dispose if access denied or sesstion has been time out
            if (AccessController.Access.StatusCode != AccessStatusCode.Ok ||
                (DateTime.Now - Tunnel.LastActivityTime).TotalSeconds > SESSION_Timeout)
                Dispose();
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            DisposeTime = DateTime.Now;

            Tunnel.OnPacketArrival -= Tunnel_OnPacketArrival;
            _pingProxy.OnPingCompleted -= PingProxy_OnPingCompleted;

            Tunnel.Dispose();
            _pingProxy.Dispose();

            _nat.Dispose();
        }
    }
}


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

namespace VpnHood.Server
{

    public class Session : IDisposable
    {
        private readonly Nat _nat;
        private readonly UdpClientFactory _udpClientFactory;
        private readonly PingProxy _pingProxy;
        private ILogger Logger => Loggers.Logger.Current;
        public Tunnel Tunnel { get; }
        public Guid ClientId { get;  }
        public Token Token { get; }
        public ClientUsage ClientUsage { get; }
        public ulong SessionId { get; }
        public Guid? SuppressedToClientId { get; internal set; }
        public DateTime CreatedTime { get; } = DateTime.Now;

        public Session(ClientInfo clientInfo, Guid clientId, UdpClientFactory udpClientFactory)
        {
            _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
            Token = clientInfo?.Token ?? throw new ArgumentNullException(nameof(clientInfo.Token));
            ClientUsage = clientInfo?.ClientUsage ?? throw new ArgumentNullException(nameof(clientInfo.ClientUsage));
            _nat = new Nat(false);
            _nat.OnNatItemRemoved += Nat_OnNatItemRemoved;
            _pingProxy = new PingProxy();
            ClientId = clientId;
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

        bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Tunnel.OnPacketArrival -= Tunnel_OnPacketArrival;
            _pingProxy.OnPingCompleted -= PingProxy_OnPingCompleted;

            Tunnel.Dispose();
            _pingProxy.Dispose();

            _nat.Dispose();
        }
    }
}


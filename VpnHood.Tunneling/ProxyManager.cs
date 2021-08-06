using PacketDotNet;
using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace VpnHood.Tunneling
{
    public abstract class ProxyManager : IDisposable
    {
        private readonly HashSet<IChannel> _channels = new();
        private readonly Nat _udpNat;
        private PingProxy _pingProxy;
        private readonly IPAddress[] _blockList = new[] {
            IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play)/SSDP (Simple Service Discovery Protocol)
        };
        public int UdpConnectionCount => _udpNat.Items.Where(x => x.Protocol == ProtocolType.Udp).Count();
        public int TcpConnectionCount => _channels.Where(x => x is not IDatagramChannel).Count();

        public ProxyManager()
        {
            _udpNat = new Nat(false);
            _udpNat.OnNatItemRemoved += Nat_OnNatItemRemoved;
        }

        protected abstract Ping CreatePing();
        protected abstract System.Net.Sockets.UdpClient CreateUdpClient();
        protected abstract void SendReceivedPacket(IPPacket ipPacket);

        private void Nat_OnNatItemRemoved(object sender, NatEventArgs e)
        {
            if (e.NatItem.Tag is UdpProxy udpProxy)
            {
                udpProxy.OnPacketReceived -= UdpProxy_OnPacketReceived;
                udpProxy.Dispose();
            }
        }

        public virtual void SendPacket(IEnumerable<IPPacket> ipPackets)
        {
            foreach (var ipPacket in ipPackets)
                SendPacket(ipPacket);
        }

        public void SendPacket(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            if (ipPacket.Version != IPVersion.IPv4) throw new Exception($"{ipPacket.Version} packets is not supported!");

            if (ipPacket.Protocol == ProtocolType.Udp)
                SendUdpPacket(ipPacket);

            else if (ipPacket.Protocol == ProtocolType.Icmp)
                SendIcmpPacket(ipPacket);

            else if (ipPacket.Protocol == ProtocolType.Tcp)
                throw new Exception("Tcp Packet should not be sent through this channel! Use TcpProxy.");

            else
                throw new Exception($"{ipPacket.Protocol} is not supported!");
        }

        private void SendIcmpPacket(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            if (_pingProxy == null)
            {
                _pingProxy = new PingProxy(CreatePing());
                _pingProxy.OnPacketReceived += PingProxy_OnPacketReceived;
            }
            _pingProxy.Send(ipPacket);
        }

        private void SendUdpPacket(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

            // drop blocke packets
            if (_blockList.Any(x => x.Equals(ipPacket.DestinationAddress)))
                return;

            // send packet via proxy
            var natItem = _udpNat.Get(ipPacket);
            if (natItem?.Tag is not UdpProxy udpProxy || udpProxy.IsDisposed)
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                udpProxy = new UdpProxy(CreateUdpClient(), new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort));
                udpProxy.OnPacketReceived += UdpProxy_OnPacketReceived;
                natItem = _udpNat.Add(ipPacket, (ushort)udpProxy.LocalPort, true);
                natItem.Tag = udpProxy;
            }
            udpProxy.Send(ipPacket);
        }

        private void PingProxy_OnPacketReceived(object sender, PacketReceivedEventArgs e)
        {
            SendReceivedPacket(e.IpPacket);
        }
        private void UdpProxy_OnPacketReceived(object sender, PacketReceivedEventArgs e)
        {
            SendReceivedPacket(e.IpPacket);
        }

        public void AddChannel(IChannel channel)
        {
            channel.OnFinished += Channel_OnFinished;
            _channels.Add(channel);
            channel.Start();
        }

        private void Channel_OnFinished(object sender, ChannelEventArgs e)
        {
            e.Channel.OnFinished -= Channel_OnFinished;
            _channels.Remove(e.Channel);
        }

        public void Dispose()
        {
            if (_pingProxy != null)
            {
                _pingProxy.OnPacketReceived -= PingProxy_OnPacketReceived;
                _pingProxy.Dispose();
            }

            _udpNat.Dispose();
            _udpNat.OnNatItemRemoved -= Nat_OnNatItemRemoved; //must be after Nat.dispose

            // dispose channels
            foreach (var channel in _channels)
                channel.Dispose();
        }

    }
}


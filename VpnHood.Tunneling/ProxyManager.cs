using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PacketDotNet;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Tunneling
{
    public abstract class ProxyManager : IDisposable
    {
        private readonly IPAddress[] _blockList =
        {
            IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play) SSDP (Simple Service Discovery Protocol)
        };

        private readonly HashSet<IChannel> _channels = new();
        private readonly Lazy<PingProxy> _pingProxy;
        private readonly Nat _udpNat;

        protected ProxyManager()
        {
            _udpNat = new Nat(false);
            _udpNat.OnNatItemRemoved += Nat_OnNatItemRemoved;
            _pingProxy = new Lazy<PingProxy>(() =>
            {
                var ret = new PingProxy(CreatePing());
                ret.OnPacketReceived += PingProxy_OnPacketReceived;
                return ret;
            });
        }

        private PingProxy PingProxy => _pingProxy.Value;

        public int UdpConnectionCount => _udpNat.Items.Count(x => x.Protocol == ProtocolType.Udp);
        // ReSharper disable once UnusedMember.Global
        public int TcpConnectionCount => _channels.Count(x => x is not IDatagramChannel);

        public void Dispose()
        {
            if (_pingProxy.IsValueCreated)
            {
                PingProxy.OnPacketReceived -= PingProxy_OnPacketReceived;
                PingProxy.Dispose();
            }

            _udpNat.Dispose();
            _udpNat.OnNatItemRemoved -= Nat_OnNatItemRemoved; //must be after Nat.dispose

            // dispose channels
            foreach (var channel in _channels)
                channel.Dispose();
        }

        protected abstract Ping CreatePing();
        protected abstract UdpClient CreateUdpClient(AddressFamily addressFamily);
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

            if (ipPacket.Protocol == ProtocolType.Udp)
                SendUdpPacket(ipPacket);

            else if (ipPacket.Protocol == ProtocolType.Icmp)
                SendIcmpPacket(ipPacket);

            else if (ipPacket.Protocol == ProtocolType.IcmpV6)
                SendIcmpPacket(ipPacket);

            else if (ipPacket.Protocol == ProtocolType.Tcp)
                throw new Exception("Tcp Packet should not be sent through this channel! Use TcpProxy.");

            else
                throw new Exception($"{ipPacket.Protocol} is not supported!");
        }

        private void SendIcmpPacket(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
            PingProxy.Send(ipPacket);
        }

        private void SendUdpPacket(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

            // drop blocked packets
            if (_blockList.Any(x => x.Equals(ipPacket.DestinationAddress)))
                return;

            // send packet via proxy
            var natItem = _udpNat.Get(ipPacket);
            if (natItem?.Tag is not UdpProxy udpProxy || udpProxy.IsDisposed)
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                udpProxy = new UdpProxy(CreateUdpClient(ipPacket.SourceAddress.AddressFamily), new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort));
                udpProxy.OnPacketReceived += UdpProxy_OnPacketReceived;
                natItem = _udpNat.Add(ipPacket, (ushort) udpProxy.LocalPort, true);
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
    }
}
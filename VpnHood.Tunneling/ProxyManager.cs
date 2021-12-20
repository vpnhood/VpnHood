using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Tunneling
{
    public abstract class ProxyManager : IDisposable
    {
        private readonly IPAddress[] _blockList =
            {
                IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play) SSDP (Simple Service Discovery Protocol)
            };

        private bool _disposed;
        private readonly HashSet<IChannel> _channels = new();
        private readonly PingProxyPool _pingProxyPool = new();
        private readonly Nat _udpNat;
        public int MaxUdpPortCount { get; set; } = 0;

        // override Handle UdpProxy.OnPacketReceived
        private class MyUdpProxy : UdpProxy
        {
            private readonly ProxyManager _proxyManager;

            public MyUdpProxy(ProxyManager proxyManager, UdpClient udpClientListener, IPEndPoint sourceEndPoint)
                : base(udpClientListener, sourceEndPoint)
            {
                _proxyManager = proxyManager;
            }

            public override Task OnPacketReceived(IPPacket ipPacket)
            {
                if (VhLogger.IsDiagnoseMode) PacketUtil.LogPacket(ipPacket, $"Delegating packet to client via {nameof(UdpProxy)}");
                return _proxyManager.OnPacketReceived(ipPacket);
            }
        }

        protected ProxyManager()
        {
            _udpNat = new Nat(false);
            _udpNat.OnNatItemRemoved += Nat_OnNatItemRemoved;
        }

        public void Cleanup()
        {
            _udpNat.Cleanup();
        }

        public TimeSpan UdpTimeout { get => _udpNat.UdpTimeout; set => _udpNat.UdpTimeout = value; }

        public int UdpConnectionCount => _udpNat.GetItemCount(ProtocolType.Udp);

        // ReSharper disable once UnusedMember.Global
        public int TcpConnectionCount
        {
            get
            {
                lock (_channels)
                    return _channels.Count(x => x is not IDatagramChannel);
            }
        }

        protected abstract UdpClient CreateUdpClient(AddressFamily addressFamily);
        protected abstract Task OnPacketReceived(IPPacket ipPacket);
        protected abstract bool IsPingSupported { get; }

        private void Nat_OnNatItemRemoved(object sender, NatEventArgs e)
        {
            if (e.NatItem.Tag is UdpProxy udpProxy)
                udpProxy.Dispose();
        }

        public virtual void SendPacket(IPPacket[] ipPackets)
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
                _ = SendIcmpPacket(ipPacket);

            else if (ipPacket.Protocol == ProtocolType.IcmpV6)
                _ = SendIcmpPacket(ipPacket);

            else if (ipPacket.Protocol == ProtocolType.Tcp)
                throw new Exception("Tcp Packet should not be sent through this channel! Use TcpProxy.");

            else
                throw new Exception($"{ipPacket.Protocol} is not supported!");
        }

        private async Task SendIcmpPacket(IPPacket ipPacket)
        {
            // validations
            if (ipPacket is null)
                throw new ArgumentNullException(nameof(ipPacket));

            if ((ipPacket.Version != IPVersion.IPv4 || ipPacket.Extract<IcmpV4Packet>()?.TypeCode != IcmpV4TypeCode.EchoRequest) &&
                (ipPacket.Version != IPVersion.IPv6 || ipPacket.Extract<IcmpV6Packet>()?.Type != IcmpV6Type.EchoRequest))
                throw new NotSupportedException($"The icmp is not supported. Packet: {PacketUtil.Format(ipPacket)}");

            if (!IsPingSupported)
                throw new NotSupportedException($"Ping is not supported by this {nameof(ProxyManager)}");

            try
            {
                // send packet via proxy
                if (VhLogger.IsDiagnoseMode)
                    PacketUtil.LogPacket(ipPacket, $"Delegating packet to host via {nameof(PingProxy)}.");
                var retPacket = await _pingProxyPool.Send(ipPacket);

                if (VhLogger.IsDiagnoseMode)
                    PacketUtil.LogPacket(ipPacket, $"Delegating packet to client via {nameof(PingProxy)}.");

                await OnPacketReceived(retPacket);
            }
            catch (Exception ex)
            {
                if (VhLogger.IsDiagnoseMode)
                    PacketUtil.LogPacket(ipPacket, $"Error in delegating echo packet via {nameof(PingProxy)}. Error: {ex.Message}", LogLevel.Error);
            }
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
                var udpCount = _udpNat.Items.Length;
                if (MaxUdpPortCount != 0 && udpCount > MaxUdpPortCount)
                {
                    VhLogger.Instance.LogWarning(GeneralEventId.Udp, $"Too many UDP port! Killing the oldest UdpProxy. {nameof(MaxUdpPortCount)}: {MaxUdpPortCount}");
                    _udpNat.RemoveOldest(ProtocolType.Udp);
                }

                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                udpProxy = new MyUdpProxy(this, CreateUdpClient(ipPacket.SourceAddress.AddressFamily), new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort));
                try
                {
                    natItem = _udpNat.Add(ipPacket, (ushort)udpProxy.LocalPort, true);
                    natItem.Tag = udpProxy;
                }
                catch { udpProxy.Dispose(); throw; }
            }

            udpProxy.Send(ipPacket);
        }

        public void AddChannel(IChannel channel)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProxyManager));

            channel.OnFinished += Channel_OnFinished;
            lock (_channels)
                _channels.Add(channel);
            channel.Start();
        }

        private void Channel_OnFinished(object sender, ChannelEventArgs e)
        {
            e.Channel.OnFinished -= Channel_OnFinished;
            lock (_channels)
                _channels.Remove(e.Channel);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _udpNat.Dispose();
            _udpNat.OnNatItemRemoved -= Nat_OnNatItemRemoved; //must be after Nat.dispose

            // dispose channels
            IChannel[] channels;
            lock (_channels)
                channels = _channels.ToArray();
            foreach (var channel in channels)
                channel.Dispose();
        }

    }
}
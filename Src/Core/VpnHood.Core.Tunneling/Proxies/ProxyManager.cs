using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Transports;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Tunneling.Proxies;

public class ProxyManager : PacketChannelPipe
{
    private readonly IPAddress[] _blockList = [
        IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play) SSDP (Simple Service Discovery Protocol)
    ];
    private readonly HashSet<IChannel> _channels = [];
    private readonly IPacketProxyPool? _pingProxyPool;
    private readonly IPacketProxyPool _udpProxyPool;

    public int PingClientCount => _pingProxyPool?.ClientCount ?? 0;
    public int UdpClientCount => _udpProxyPool.ClientCount;

    public int TcpConnectionCount {
        get {
            lock (_channels) return _channels.Count(x => x is not IDatagramChannel);
        }
    }

    public ProxyManager(ISocketFactory socketFactory, ProxyManagerOptions options)
        : base(options.AutoDisposePackets)
    {
        var udpProxyPoolOptions = new UdpProxyPoolOptions {
            PacketProxyCallbacks = options.PacketProxyCallbacks,
            SocketFactory = socketFactory,
            UdpTimeout = options.UdpTimeout,
            MaxClientCount = options.MaxUdpClientCount,
            LogScope = options.LogScope,
            PacketQueueCapacity = options.PacketQueueCapacity,
            SendBufferSize = options.UdpSendBufferSize,
            ReceiveBufferSize = options.UdpReceiveBufferSize,
            AutoDisposePackets = options.AutoDisposePackets
        };

        // create UDP proxy pool
        _udpProxyPool = options.UseUdpProxy2
            ? new UdpProxyPoolEx(udpProxyPoolOptions)
            : new UdpProxyPool(udpProxyPoolOptions);
        _udpProxyPool.PacketReceived += Proxy_PacketReceived;

        // create Ping proxy pools
        if (options.IsPingSupported) {
            _pingProxyPool = new PingProxyPool(new PingProxyPoolOptions {
                PacketProxyCallbacks = options.PacketProxyCallbacks,
                IcmpTimeout = options.IcmpTimeout,
                MaxClientCount = options.MaxPingClientCount,
                AutoDisposePackets = options.AutoDisposePackets,
                LogScope = options.LogScope
            });
            _pingProxyPool.PacketReceived += Proxy_PacketReceived;
        }
    }

    private void Proxy_PacketReceived(object sender, PacketReceivedEventArgs e)
    {
        OnPacketReceived(e);
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        // Drop blocked packets
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < _blockList.Length; i++) {
            var ipAddress = _blockList[i];
            if (ipAddress.Equals(ipPacket.DestinationAddress))
                throw new Exception("The packet is blocked.");
        }

        switch (ipPacket.Protocol) {
            case IpProtocol.Udp:
                _udpProxyPool.SendPacketQueued(ipPacket);
                break;

            case IpProtocol.IcmpV4:
            case IpProtocol.IcmpV6:
                if (_pingProxyPool == null)
                    throw new NotSupportedException("Ping is not supported by this proxy.");
                _pingProxyPool.SendPacketQueued(ipPacket);
                break;

            default:
                throw new Exception($"{ipPacket.Protocol} packet should not be sent through this channel.");
        }
    }

    public void AddChannel(IChannel channel)
    {
        if (Disposed)
            throw new ObjectDisposedException(nameof(ProxyManager));

        lock (_channels)
            _channels.Add(channel);
        channel.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            // dispose udp proxy pool
            _udpProxyPool.PacketReceived -= Proxy_PacketReceived;
            _udpProxyPool.Dispose();

            // dispose ping proxy pool
            if (_pingProxyPool != null) {
                _pingProxyPool.PacketReceived -= Proxy_PacketReceived;
                _pingProxyPool.Dispose();
            }

            // dispose channels
            lock (_channels)
                foreach (var channel in _channels)
                    channel.DisposeAsync(false);
        }
    }
}
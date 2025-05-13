using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public class ProxyManager : IPacketSenderQueued, IAsyncDisposable
{
    private readonly IPAddress[] _blockList = [
        IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play) SSDP (Simple Service Discovery Protocol)
    ];
    private readonly HashSet<IChannel> _channels = [];
    private readonly IPacketProxyPool? _pingProxyPool;
    private readonly IPacketProxyPool _udpProxyPool;
    private readonly bool _autoDisposeSentPackets;
    private bool _disposed;

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public int PingClientCount => _pingProxyPool?.ClientCount ?? 0;
    public int UdpClientCount => _udpProxyPool.ClientCount;

    public int TcpConnectionCount {
        get {
            lock (_channels) return _channels.Count(x => x is not IDatagramChannel);
        }
    }

    public ProxyManager(ISocketFactory socketFactory, ProxyManagerOptions options)
    {
        _autoDisposeSentPackets = options.AutoDisposeSentPackets;

        var udpProxyPoolOptions = new UdpProxyPoolOptions {
            PacketProxyCallbacks = options.PacketProxyCallbacks,
            SocketFactory = socketFactory,
            UdpTimeout = options.UdpTimeout  ,
            MaxClientCount = options.MaxUdpClientCount,
            LogScope = options.LogScope,
            PacketQueueCapacity = options.PacketQueueCapacity,
            SendBufferSize = options.UdpSendBufferSize,
            ReceiveBufferSize = options.UdpReceiveBufferSize,
            AutoDisposeSentPackets = options.AutoDisposeSentPackets
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
                AutoDisposeSentPackets = options.AutoDisposeSentPackets,
                LogScope = options.LogScope
            });
            _pingProxyPool.PacketReceived += Proxy_PacketReceived;
        }
    }

    private void Proxy_PacketReceived(object sender, PacketReceivedEventArgs e)
    {
        PacketReceived?.Invoke(this, e);
    }

    public void SendPacketQueued(IpPacket ipPacket)
    {
        try {
            // send packet via proxy
            PacketLogger.LogPacket(ipPacket, $"Delegating packet to host via {GetType().Name}.");
            SendPacketQueuedInternal(ipPacket);
        }
        catch (Exception ex) {
            // Log the error
            PacketLogger.LogPacket(ipPacket, $"Error while sending packet via {GetType().Name}.", exception: ex);

            // Dispose the packet if needed
            if (_autoDisposeSentPackets)
                ipPacket.Dispose();
        }
    }

    private void SendPacketQueuedInternal(IpPacket ipPacket)
    {
        // drop blocked packets
        if (_blockList.Any(x => x.Equals(ipPacket.DestinationAddress)))
            throw new Exception("The packet is blocked.");

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
        if (_disposed) throw new ObjectDisposedException(nameof(ProxyManager));

        lock (_channels)
            _channels.Add(channel);
        channel.Start();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        PacketReceived = null;

        // dispose udp proxy pool
        _udpProxyPool.PacketReceived -= Proxy_PacketReceived;
        _udpProxyPool.Dispose();

        // dispose ping proxy pool
        if (_pingProxyPool != null) {
            _pingProxyPool.PacketReceived -= Proxy_PacketReceived;
            _pingProxyPool.Dispose();
        }

        // dispose channels
        Task[] disposeTasks;
        lock (_channels)
            disposeTasks = _channels.Select(channel => channel.DisposeAsync(false).AsTask()).ToArray();

        await Task.WhenAll(disposeTasks).VhConfigureAwait();
        _disposed = true;
    }
}
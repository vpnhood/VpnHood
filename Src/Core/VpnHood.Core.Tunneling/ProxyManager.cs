using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public class ProxyManager : IAsyncDisposable
{
    private readonly IPAddress[] _blockList = [
        IPAddress.Parse("239.255.255.250") //  UPnP (Universal Plug and Play) SSDP (Simple Service Discovery Protocol)
    ];
    private readonly HashSet<IChannel> _channels = [];
    private readonly IPacketProxyPool? _pingProxyPool;
    private readonly IPacketProxyPool _udpProxyPool;
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
        var udpProxyPoolOptions = new UdpProxyPoolOptions {
            PacketProxyCallbacks = options.PacketProxyCallbacks,
            SocketFactory =  socketFactory, 
            UdpTimeout = options.UdpTimeout, 
            MaxClientCount = options.MaxUdpClientCount,
            LogScope = options.LogScope,
            PacketQueueCapacity = options.PacketQueueCapacity, 
            SendBufferSize = options.UdpSendBufferSize, 
            ReceiveBufferSize = options.UdpReceiveBufferSize,
        };

        // create UDP proxy pool
        _udpProxyPool = options.UseUdpProxy2
            ? new UdpProxyPoolEx(udpProxyPoolOptions)
            : new UdpProxyPool(udpProxyPoolOptions);
        _udpProxyPool.PacketReceived += Proxy_PacketReceived;

        // create Ping proxy pools
        if (options.IsPingSupported) {
            _pingProxyPool = new PingProxyPool(options.PacketProxyCallbacks, 
                icmpTimeout:options.IcmpTimeout,
                maxClientCount:options.MaxIcmpClientCount,
                logScope: options.LogScope);
            _pingProxyPool.PacketReceived += Proxy_PacketReceived;
        }
    }

    private void Proxy_PacketReceived(object sender, PacketReceivedEventArgs e)
    {
        PacketReceived?.Invoke(this, e);
    }

    public void SendPacketsQueued(IList<IpPacket> ipPackets)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            SendPacketQueued(ipPacket);
        }
    }

    public void SendPacketQueued(IpPacket ipPacket)
    {
        if (ipPacket is null)
            throw new ArgumentNullException(nameof(ipPacket));

        // drop blocked packets
        if (_blockList.Any(x => x.Equals(ipPacket.DestinationAddress))) {
            PacketLogger.LogPacket(ipPacket, $"Blocked a packet. Dst: {ipPacket.DestinationAddress}", 
                logLevel: LogLevel.Debug, eventId: GeneralEventId.NetFilter);
            return;
        }

        // send packet via proxy
        if (VhLogger.IsDiagnoseMode)
            PacketLogger.LogPacket(ipPacket, "Delegating packet to host via proxy.");

        try {
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
        catch (Exception ex) when (ex is ISelfLog) {
        }
        catch (Exception ex) {
            PacketLogger.LogPacket(ipPacket, "Error in delegating packet via proxy.", LogLevel.Error, ex);
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
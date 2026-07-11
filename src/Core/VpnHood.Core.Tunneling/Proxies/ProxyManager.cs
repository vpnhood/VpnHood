using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Exceptions;

namespace VpnHood.Core.Tunneling.Proxies;

public class ProxyManager : PassthroughPacketTransport
{
    private readonly List<ProxyChannel> _streamProxyChannels = [];
    private readonly IPacketProxyPool? _pingProxyPool;
    private readonly IPacketProxyPool _udpProxyPool;
    private readonly Job _cleanupJob;
    private Traffic _removedChannelsTraffic = new();

    public bool IsIpV6Supported { get; set; } = true;
    public int PingClientCount => _pingProxyPool?.ClientCount ?? 0;
    public int UdpClientCount => _udpProxyPool.ClientCount;

    public int TcpConnectionCount {
        get {
            lock (_streamProxyChannels)
                return _streamProxyChannels.Count;
        }
    }

    public Traffic Traffic {
        get {
            lock (_streamProxyChannels) {
                var traffic = new Traffic(PacketStat.SentBytes, PacketStat.ReceivedBytes) + _removedChannelsTraffic;
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var i = 0; i < _streamProxyChannels.Count; i++)
                    traffic += _streamProxyChannels[i].Traffic;

                return traffic;
            }
        }
    }


    public ProxyManager(ISocketFactory socketFactory, ProxyManagerOptions options)
    {
        var udpProxyPoolOptions = new UdpProxyPoolOptions {
            PacketProxyCallbacks = options.PacketProxyCallbacks,
            SocketFactory = socketFactory,
            UdpTimeout = options.UdpTimeout,
            MaxClientCount = options.MaxUdpClientCount,
            LogScope = options.LogScope,
            PacketQueueCapacity = options.PacketQueueCapacity,
            BufferSize = options.UdpBufferSize,
            AutoDisposePackets = options.AutoDisposePackets
        };

        // create UDP proxy pool
        _udpProxyPool = options.UseUdpProxy2
            ? new UdpProxyPoolEx(udpProxyPoolOptions)
            : new UdpProxyPool(udpProxyPoolOptions);
        _udpProxyPool.PacketReceived += Proxy_PacketReceived;

        // Sweep dead stream channels out of the tracking list. A ProxyChannel disposes ITSELF when either
        // side dies (its CheckAlive job), but stayed in _streamProxyChannels until the manager was disposed,
        // pinning the whole per-flow object graph (local TCP connection buffers + host socket). Long
        // sessions with many bypassed flows (split-country) leaked this into the iOS jetsam limit.
        _cleanupJob = new Job(Cleanup, nameof(ProxyManager));

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

    private void Proxy_PacketReceived(object? sender, IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        if (ipPacket.IsV6() && !IsIpV6Supported)
            throw new PacketDropException("IPv6 is not supported.");

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

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        CleanupChannels();
        return default;
    }

    public void CleanupChannels()
    {
        lock (_streamProxyChannels) {
            // Only Disposed channels are swept: a dead-but-undisposed channel disposes itself via its own
            // CheckAlive job, and skipping NotStarted avoids racing AddChannel's post-add Start().
            for (var i = _streamProxyChannels.Count - 1; i >= 0; i--) {
                var channel = _streamProxyChannels[i];
                if (channel.State != PacketChannelState.Disposed)
                    continue;

                // keep Traffic monotonic after the channel is dropped from the sum
                _removedChannelsTraffic += channel.Traffic;
                _streamProxyChannels.RemoveAt(i);
            }
        }
    }

    public void AddChannel(ProxyChannel channel, bool disposeOnFail)
    {
        try {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(ProxyManager));

            lock (_streamProxyChannels)
                _streamProxyChannels.Add(channel);
            channel.Start();
        }
        catch {
            if (disposeOnFail)
                channel.Dispose();
            throw;
        }
    }

    protected override void DisposeManaged()
    {
        _cleanupJob.Dispose();

        // dispose udp proxy pool
        _udpProxyPool.PacketReceived -= Proxy_PacketReceived;
        _udpProxyPool.Dispose();

        // dispose ping proxy pool
        if (_pingProxyPool != null) {
            _pingProxyPool.PacketReceived -= Proxy_PacketReceived;
            _pingProxyPool.Dispose();
        }

        // dispose channels
        lock (_streamProxyChannels)
            foreach (var channel in _streamProxyChannels)
                channel.Dispose();

        base.DisposeManaged();
    }
}
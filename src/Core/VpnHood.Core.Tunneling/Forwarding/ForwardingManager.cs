using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Exceptions;

namespace VpnHood.Core.Tunneling.Forwarding;

public class ForwardingManager : PassthroughPacketTransport
{
    private readonly List<StreamForwardingChannel> _streamForwardingChannels = [];
    private readonly IPacketForwarderPool? _pingForwarderPool;
    private readonly IPacketForwarderPool _udpForwarderPool;
    private readonly Job _cleanupJob;
    private Traffic _removedChannelsTraffic = new();

    public bool IsIpV6Supported { get; set; } = true;
    public int PingClientCount => _pingForwarderPool?.ClientCount ?? 0;
    public int UdpClientCount => _udpForwarderPool.ClientCount;

    public int TcpConnectionCount {
        get {
            lock (_streamForwardingChannels)
                return _streamForwardingChannels.Count;
        }
    }

    public Traffic Traffic {
        get {
            lock (_streamForwardingChannels) {
                var traffic = new Traffic(PacketStat.SentBytes, PacketStat.ReceivedBytes) + _removedChannelsTraffic;
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var i = 0; i < _streamForwardingChannels.Count; i++)
                    traffic += _streamForwardingChannels[i].Traffic;

                return traffic;
            }
        }
    }


    public ForwardingManager(ISocketFactory socketFactory, ForwardingManagerOptions options)
    {
        var udpForwarderPoolOptions = new UdpForwarderPoolOptions {
            PacketForwarderCallbacks = options.PacketForwarderCallbacks,
            SocketFactory = socketFactory,
            UdpTimeout = options.UdpTimeout,
            MaxClientCount = options.MaxUdpClientCount,
            MaxDnsClientCount = options.MaxUdpDnsClientCount,
            LogScope = options.LogScope,
            PacketQueueCapacity = options.PacketQueueCapacity,
            BufferSize = options.UdpBufferSize
        };

        // create UDP forwarder pool
        _udpForwarderPool = new UdpForwarderPool(udpForwarderPoolOptions);
        _udpForwarderPool.PacketReceived += Forwarder_PacketReceived;

        // Sweep dead stream channels out of the tracking list. A StreamForwardingChannel disposes ITSELF when either
        // side dies (its CheckAlive job), but stayed in _streamForwardingChannels until the manager was disposed,
        // pinning the whole per-flow object graph (local TCP connection buffers + host socket). Long
        // sessions with many bypassed flows (split-country) leaked this into the iOS jetsam limit.
        _cleanupJob = new Job(Cleanup, nameof(ForwardingManager));

        // create Ping forwarder pools
        if (options.IsPingSupported) {
            _pingForwarderPool = new PingForwarderPool(new PingForwarderPoolOptions {
                PacketForwarderCallbacks = options.PacketForwarderCallbacks,
                IcmpTimeout = options.IcmpTimeout,
                MaxClientCount = options.MaxPingClientCount,
                LogScope = options.LogScope
            });
            _pingForwarderPool.PacketReceived += Forwarder_PacketReceived;
        }
    }

    private void Forwarder_PacketReceived(object? sender, IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    protected override void SendPacket(IpPacket ipPacket)
    {
        if (ipPacket.IsV6 && !IsIpV6Supported)
            throw new PacketDropException("IPv6 is not supported.");

        switch (ipPacket.Protocol) {
            case IpProtocol.Udp:
                _udpForwarderPool.SendPacketQueued(ipPacket);
                break;

            case IpProtocol.IcmpV4:
            case IpProtocol.IcmpV6:
                if (_pingForwarderPool == null)
                    throw new NotSupportedException("Ping is not supported by this forwarder.");
                _pingForwarderPool.SendPacketQueued(ipPacket);
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
        lock (_streamForwardingChannels) {
            // Only Disposed channels are swept: a dead-but-undisposed channel disposes itself via its own
            // CheckAlive job, and skipping NotStarted avoids racing AddChannel's post-add Start().
            for (var i = _streamForwardingChannels.Count - 1; i >= 0; i--) {
                var channel = _streamForwardingChannels[i];
                if (channel.State != PacketChannelState.Disposed)
                    continue;

                // keep Traffic monotonic after the channel is dropped from the sum
                _removedChannelsTraffic += channel.Traffic;
                _streamForwardingChannels.RemoveAt(i);
            }
        }
    }

    public void AddChannel(StreamForwardingChannel channel, bool disposeOnFail)
    {
        try {
            // under the lock of the close's sweep: the channel joins before the sweep, which closes it, or is refused
            lock (_streamForwardingChannels) {
                ObjectDisposedException.ThrowIf(IsDisposed || IsDisposing, this);
                _streamForwardingChannels.Add(channel);
            }

            channel.Start();
        }
        catch {
            // in practice an add fails only at the manager's end, which gives its channels the quick close too
            if (disposeOnFail)
                channel.Dispose();
            throw;
        }
    }

    protected override void DisposeManaged()
    {
        _cleanupJob.Dispose();

        // dispose udp forwarder pool
        _udpForwarderPool.PacketReceived -= Forwarder_PacketReceived;
        _udpForwarderPool.Dispose();

        // dispose ping forwarder pool
        if (_pingForwarderPool != null) {
            _pingForwarderPool.PacketReceived -= Forwarder_PacketReceived;
            _pingForwarderPool.Dispose();
        }

        // dispose channels
        lock (_streamForwardingChannels)
            VhUtils.DisposeAll(_streamForwardingChannels);

        base.DisposeManaged();
    }
}
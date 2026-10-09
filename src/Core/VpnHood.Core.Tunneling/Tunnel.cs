using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.PacketTransports;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling;

// Unthrottled, the tunnel passes each packet straight on to its channel. With a send limit, the session's
// packets wait in the tunnel's own queue, one for all its channels, and leave it at the limit: the
// session's bottleneck, sharing the limit with its proxy streams. The queue holds about a quarter second of
// the limit, at least a new connection's first burst, and drops its oldest packet when full, so the newer
// ones behind it show the loss
public class Tunnel : PacketTransportBase
{
    private const int MinThrottledQueueCapacity = 32;
    private readonly ChannelManager _channelManager;
    private readonly bool _isThrottled;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;

    public TrafficMeter TrafficMeter { get; }
    public int PacketChannelCount => _channelManager.PacketChannelCount;
    public int StreamProxyChannelCount => _channelManager.ProxyChannelCount;
    public void RemoveChannels<T>() where T : IChannel => _channelManager.RemoveChannels<T>();
    public IReadOnlyList<IPacketChannel> PacketChannels => _channelManager.PacketChannels;

    public int MaxPacketChannelCount {
        get => _channelManager.MaxPacketChannelCount;
        set => _channelManager.MaxPacketChannelCount = value;
    }

    public int Mtu { get; set; }

    public Tunnel(TunnelOptions options)
        : base(CreatePacketTransportOptions(options), singleMode: true, passthrough: options.MaxSpeed.Sent <= 0)
    {
        _isThrottled = options.MaxSpeed.Sent > 0;
        _cancellationToken = _cancellationTokenSource.Token;
        Mtu = options.Mtu;
        TrafficMeter = new TrafficMeter {
            MaxSpeed = options.MaxSpeed,
            MaxSpeedBurst = options.MaxSpeedBurst
        };
        _channelManager = new ChannelManager(options.MaxPacketChannelCount, Channel_OnPacketReceived);
    }

    private static PacketTransportOptions CreatePacketTransportOptions(TunnelOptions options)
    {
        return new PacketTransportOptions {
            AutoDisposePackets = false, // a packet handed to a channel is the channel's
            Blocking = false,
            QueueCapacity = options.MaxSpeed.Sent > 0
                ? (int)Math.Clamp(options.MaxSpeed.Sent / 4 / TransportDefaults.MaxPacketSize,
                    MinThrottledQueueCapacity, PacketTransportOptions.DefaultQueueCapacity)
                : null
        };
    }

    public void AddChannel(IChannel channel, bool disposeIfFailed = false)
    {
        try {
            _channelManager.AddChannel(channel);
        }
        catch when (disposeIfFailed) {
            channel.Dispose();
            throw;
        }
    }

    private void Channel_OnPacketReceived(object? sender, IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    // One packet at a time: the caller's, passed through, or the queue's when throttled. It is not thread-safe
    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        var ipPacket = ipPackets[0];
        if (!_isThrottled) {
            SendPacketToChannel(ipPacket);
            return default;
        }

        // sent, then paid for: the wait holds the next packets in the queue, where a full queue drops the
        // oldest. The length is read first, as the channel may send and dispose the packet at once
        var packetLength = ipPacket.PacketLength;
        if (!SendPacketToChannel(ipPacket))
            return default;

        var throttleTask = TrafficMeter.ThrottleSendAsync(packetLength, _cancellationToken);
        return throttleTask.IsCompletedSuccessfully ? default : WaitForThrottle(throttleTask);
    }

    private async ValueTask WaitForThrottle(ValueTask throttleTask)
    {
        try {
            await throttleTask.Vhc();
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested) {
            // disposed: the packets left in the queue are disposed with it
        }
    }

    // a packet that cannot go is disposed here, as no one else holds it; one a channel refused is disposed
    // already, and a second Dispose does nothing
    private bool SendPacketToChannel(IpPacket ipPacket)
    {
        try {
            var channel = FindChannelForPacket(ipPacket);
            VerifyMtu(ipPacket, TransportDefaults.MtuOverhead);
            channel.SendPacketQueued(ipPacket);
            return true;
        }
        catch (Exception ex) {
            LogPacket(ipPacket, ex, "Could not send a packet to a channel.");
            ipPacket.Dispose();
            return false;
        }
    }

    private void VerifyMtu(IpPacket ipPacket, int overheadSize)
    {
        // use RemoteMtu if the channel is streamed
        if (ipPacket.PacketLength + overheadSize <= Mtu)
            return;

        var replyPacket = PacketBuilder.BuildIcmpPacketTooBigReply(ipPacket, (ushort)Mtu);
        OnPacketReceived(replyPacket);
        throw new Exception(
            $"The packet length is larger than MTU. PacketLength: {ipPacket.PacketLength}, MTU: {Mtu}.");
    }

    private IPacketChannel FindChannelForPacket(IpPacket ipPacket)
    {
        // remove channel if it is not connected
        var channel = FindChannelForPacketInternal(ipPacket);
        while (channel.State != PacketChannelState.Connected) {
            _channelManager.CleanupChannels();
            channel = FindChannelForPacketInternal(ipPacket);
        }

        return channel;
    }

    private IPacketChannel FindChannelForPacketInternal(IpPacket ipPacket)
    {
        // find channel by protocol
        var channelCount = _channelManager.PacketChannelCount;
        var channelIndex = channelCount switch {
            0 => throw new Exception("No available PacketChannel to send packets."),
            1 => 0,
            _ => ipPacket.Protocol switch {
                // select channel by tcp source port
                IpProtocol.Tcp => ipPacket.ExtractTcp().SourcePort % channelCount,

                // select channel by udp source port
                IpProtocol.Udp => ipPacket.ExtractUdp().SourcePort % channelCount,

                // select the first channel for other protocols
                _ => 0
            }
        };

        return _channelManager.GetPacketChannel(channelIndex);
    }

    protected override void DisposeManaged()
    {
        // ends a throttle wait first
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();
        _channelManager.Dispose();
        TrafficMeter.Dispose();

        base.DisposeManaged();
    }
}

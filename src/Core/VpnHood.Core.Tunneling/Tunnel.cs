using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.PacketTransports;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling;

// A session's tunnel: it passes each packet straight on to one of its channels. A session with a send limit
// uses a ThrottledTunnel, which paces the packets first
public class Tunnel : PacketTransportBase
{
    private readonly ChannelManager _channelManager;

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
        : this(options, throttlesPackets: false)
    {
    }

    // A tunnel with a send limit must pace its packets, or its proxy streams would keep the limit and its packets
    // would not; one that paces them needs a limit to pace them to
    protected Tunnel(TunnelOptions options, bool throttlesPackets)
        : base(new PacketTransportOptions {
            AutoDisposePackets = false, // a packet handed on is no longer the tunnel's
            Blocking = false
        }, singleMode: true, passthrough: true)
    {
        if ((options.MaxSpeed.Sent > 0) != throttlesPackets)
            throw new ArgumentException(throttlesPackets
                ? "A tunnel that throttles its packets needs a send limit."
                : "A tunnel with a send limit must throttle its packets.", nameof(options));

        Mtu = options.Mtu;
        TrafficMeter = new TrafficMeter {
            MaxSpeed = options.MaxSpeed,
            MaxSpeedBurst = options.MaxSpeedBurst
        };
        _channelManager = new ChannelManager(options.MaxPacketChannelCount, Channel_OnPacketReceived);
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

    // one packet at a time, under the base's lock
    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        SendPacketToChannel(ipPackets[0]);
        return default;
    }

    // A packet that cannot go is disposed here, as no one else holds it; one a channel refused is disposed
    // already, and a second Dispose does nothing. A too-big packet's ICMP reply goes to the receive handlers on
    // the caller's thread, so a handler that waits holds the caller: for a throttled tunnel, every turn
    protected bool SendPacketToChannel(IpPacket ipPacket)
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
        _channelManager.Dispose();
        TrafficMeter.Dispose();

        base.DisposeManaged();
    }
}

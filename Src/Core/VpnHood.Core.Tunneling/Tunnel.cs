using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Tunneling;

public class Tunnel : PassthroughPacketTransport
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
    {
        Mtu = options.Mtu;
        TrafficMeter = new TrafficMeter();
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

    // it is not thread-safe
    protected override void SendPacket(IpPacket ipPacket)
    {
        // flush all packets to the same channel
        var channel = FindChannelForPacket(ipPacket);

        // check is there any packet larger than MTU
        VerifyMtu(ipPacket, TunnelDefaults.MtuOverhead);

        // send packet
        channel.SendPacketQueued(ipPacket);
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
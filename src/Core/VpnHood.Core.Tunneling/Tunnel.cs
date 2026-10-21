using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.PacketTransports;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling;

// A session's tunnel: it passes each packet straight on, to its UDP channel while UDP is on and otherwise to one of
// its stream channels, and hands up what either receives. A session with a send limit uses a ThrottledTunnel, which
// paces the packets first; the limit and the traffic count are the session's
public class Tunnel : PacketTransportBase
{
    private readonly ChannelManager _channelManager;
    private readonly IPacketChannel? _udpChannel;
    private volatile bool _useUdp;

    public int PacketChannelCount => _channelManager.PacketChannelCount;
    public int StreamProxyChannelCount => _channelManager.ProxyChannelCount;
    public void RemoveChannels<T>(bool dispose = true) where T : IChannel => _channelManager.RemoveChannels<T>(dispose);
    // a copy, as channels are added and cleaned up under the reader
    public IReadOnlyList<IStreamPacketChannel> GetPacketChannels() => _channelManager.GetPacketChannels();

    public int MaxPacketChannelCount {
        get => _channelManager.MaxPacketChannelCount;
        set => _channelManager.MaxPacketChannelCount = value;
    }

    public int Mtu { get; set; }

    // UDP on sends every packet on the UDP channel; the stream channels stay, and take the packets again once it is
    // off. Only a tunnel with a UDP channel can turn it on
    public bool UseUdp {
        get => _useUdp;
        set {
            if (value && _udpChannel is null)
                throw new InvalidOperationException("The tunnel has no UDP channel.");

            _useUdp = value;
        }
    }

    public Tunnel(TunnelOptions options)
        : base(new PacketTransportOptions {
            AutoDisposePackets = false, // a packet handed on is no longer the tunnel's
            Blocking = false
        }, singleMode: true, passthrough: true)
    {
        Mtu = options.Mtu;
        _channelManager = new ChannelManager(options.MaxPacketChannelCount, Channel_OnPacketReceived);
        _udpChannel = options.UdpChannel;
        if (_udpChannel != null) {
            _udpChannel.PacketReceived += Channel_OnPacketReceived;
            _udpChannel.Start();
        }
    }

    public void AddChannel(IChannel channel, bool disposeIfFailed = false)
    {
        try {
            _channelManager.AddChannel(channel);
        }
        catch when (disposeIfFailed) {
            // in practice a join fails only at the tunnel's end, which gives its channels the quick close too
            channel.Dispose();
            throw;
        }
    }

    private void Channel_OnPacketReceived(object? sender, IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    // One packet at a time, under the base's lock. One that could not go still counts as sent in the tunnel's own
    // stats; left so, as nothing reads them
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
            return channel.SendPacketQueued(ipPacket);
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
        if (_useUdp && _udpChannel is { } udpChannel)
            return udpChannel;

        // a channel that is not connected is cleaned up, and the packet takes another
        var channel = _channelManager.GetPacketChannel(ipPacket);
        while (channel.State != PacketChannelState.Connected) {
            _channelManager.CleanupChannels();
            channel = _channelManager.GetPacketChannel(ipPacket);
        }

        return channel;
    }

    protected override void DisposeManaged()
    {
        // the UDP channel first: a datagram meanwhile goes up to no one
        if (_udpChannel != null) {
            _udpChannel.PacketReceived -= Channel_OnPacketReceived;
            _udpChannel.Dispose();
        }

        _channelManager.Dispose();
        base.DisposeManaged();
    }
}

using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.PacketTransports;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Tunneling;

/// <summary>
/// Represents a session's tunnel: it passes each packet straight on, to its UDP channel while UDP is on and otherwise to
/// one of its stream channels, and hands up what either receives.
/// </summary>
/// <remarks>
/// A session with a send limit uses a <see cref="ThrottledTunnel"/>, which paces the packets first; the limit and the
/// traffic count are the session's.
/// </remarks>
public class Tunnel : PacketTransportBase
{
    private readonly ChannelManager _channelManager;
    private readonly IPacketChannel? _udpChannel;
    private volatile bool _useUdp;

    /// <summary>
    /// Gets the number of packet channels the tunnel holds, its UDP channel aside; a removed one still closing is not
    /// counted.
    /// </summary>
    public int PacketChannelCount => _channelManager.PacketChannelCount;

    /// <summary>Gets the number of stream forwarding channels the tunnel holds.</summary>
    public int StreamForwardingChannelCount => _channelManager.StreamForwardingChannelCount;

    /// <summary>Removes the tunnel's channels of a type, its UDP channel aside.</summary>
    /// <typeparam name="T">The channels' type.</typeparam>
    /// <param name="dispose"><see langword="true"/>, the default, to dispose them too.</param>
    /// <exception cref="ObjectDisposedException">The tunnel is disposed, and it has such a channel.</exception>
    public void RemoveChannels<T>(bool dispose = true) where T : IChannel => _channelManager.RemoveChannels<T>(dispose);

    /// <summary>Gets the tunnel's packet channels.</summary>
    /// <returns>A copy of the list, as channels are added and cleaned up meanwhile.</returns>
    public IReadOnlyList<IStreamPacketChannel> GetPacketChannels() => _channelManager.GetPacketChannels();

    /// <summary>Gets or sets the most packet channels the tunnel holds at once.</summary>
    /// <exception cref="ArgumentException">The value is less than 1.</exception>
    public int MaxPacketChannelCount {
        get => _channelManager.MaxPacketChannelCount;
        set => _channelManager.MaxPacketChannelCount = value;
    }

    /// <summary>
    /// Gets or sets the largest packet the other end takes, as the packet is: a larger one that has a channel to go on
    /// gets an ICMP "packet too big" reply, which names this size, and is dropped.
    /// </summary>
    public int Mtu { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every packet goes on the UDP channel; the stream channels stay, and take
    /// the packets again once it is off.
    /// </summary>
    /// <remarks>A server's stream channel whose close was lost stays until the session ends.</remarks>
    /// <exception cref="InvalidOperationException">It is set on, and the tunnel has no UDP channel.</exception>
    // the server has no timeout for an established stream channel: left so
    public bool UseUdp {
        get => _useUdp;
        set {
            if (value && _udpChannel is null)
                throw new InvalidOperationException("The tunnel has no UDP channel.");

            _useUdp = value;
        }
    }

    /// <summary>Initializes a new instance of the <see cref="Tunnel"/> class.</summary>
    /// <param name="options">The options.</param>
    /// <exception cref="ArgumentException">The options' most packet channels is less than 1.</exception>
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

    /// <summary>
    /// Takes a channel and starts it: a packet channel, which carries packets, or a stream forwarding channel. The tunnel
    /// disposes it at its own end.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="disposeIfFailed"><see langword="true"/> to dispose the channel where it cannot be taken.</param>
    /// <remarks>
    /// Where the tunnel then holds more packet channels than its most, its closing and dead ones go first, then its
    /// oldest.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The channel is neither a stream packet channel nor a stream forwarding channel.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The tunnel is disposed.</exception>
    /// <exception cref="InvalidOperationException">The channel is started already.</exception>
    /// <exception cref="Exception">The tunnel holds the channel already.</exception>
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

    /// <inheritdoc/>
    /// <remarks>
    /// One packet at a time, under the base's lock. One that could not go still counts as sent in the tunnel's own
    /// stats.
    /// </remarks>
    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        SendPacketToChannel(ipPackets[0]);
        return default;
    }

    // A packet that cannot go is disposed here, as no one else holds it; one a channel refused is disposed
    // already, and a second Dispose does nothing. A too-big packet's ICMP reply goes to the receive handlers on
    // the caller's thread, so a handler that waits holds the caller: for a throttled tunnel, every turn
    private protected bool SendPacketToChannel(IpPacket ipPacket)
    {
        try {
            var channel = FindChannelForPacket(ipPacket);
            VerifyMtu(ipPacket);
            return channel.SendPacketQueued(ipPacket);
        }
        catch (Exception ex) {
            LogPacket(ipPacket, ex, "Could not send a packet to a channel.");
            ipPacket.Dispose();
            return false;
        }
    }

    // Mtu is the largest packet the other end takes, as the packet is: the channel's overhead is not the packet's. The
    // reply names that same limit, as one that names a larger one leaves a sender that adapts still sending too big
    private void VerifyMtu(IpPacket ipPacket)
    {
        if (ipPacket.PacketLength <= Mtu)
            return;

        var replyPacket = PacketBuilder.BuildIcmpPacketTooBigReply(ipPacket, Mtu);
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

    /// <inheritdoc/>
    /// <remarks>It disposes its UDP channel, then its other channels.</remarks>
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

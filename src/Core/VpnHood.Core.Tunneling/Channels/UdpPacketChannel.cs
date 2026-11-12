using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling.Cryptography;
using VpnHood.Core.Tunneling.DatagramMessaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Channels;

/// <summary>
/// Provides a session's UDP, for the session's whole life: its keys, its send count and a send queue of its own.
/// </summary>
/// <remarks>
/// Its one send loop packs the queued packets into datagrams and seals each with the next sequence number, so the
/// count and the send key need no lock. Its tunnel sends on it while UDP is on; what it receives goes up to the
/// tunnel, on or off. It has no connection: it carries packets from its creation until it is disposed, so it has
/// nothing to start and no close of its own, and its tunnel holds it beside the channel manager, never in it.
/// </remarks>
public abstract class UdpPacketChannel : PacketTransport, IPacketChannel
{
    // what a datagram carries at most: one packet as big as a tunnel carries, so the datagram still fits a 1500-byte path
    private const int MaxPayloadLength = TransportDefaults.MtuClient;

    // what a datagram of several packets carries at most: small packets that share one must not make it bigger than a
    // single packet needs, or they stop crossing a path a little under 1500 bytes; only a packet too big to share
    // makes a bigger datagram, alone
    private const int MaxBatchLength = 1380;

    private readonly bool _isServer;
    // Compatibility (2026-10): a server's session below protocol 14 seals the legacy way; drop with UdpDatagram's
    private readonly bool _isLegacy;
    private readonly AesGcmCryptor _sendCryptor;
    private readonly AesGcmCryptor _receiveCryptor;
    private readonly TrafficMeter? _trafficMeter;
    private ulong _nextSendSequenceNumber;
    private byte[]? _sendBuffer;
    private ReplayWindow? _replayWindow; // made with the first datagram, so a session on TCP holds none

    internal ulong SessionId { get; }

    /// <summary>Gets the sequence number of the channel's next datagram: the ones sent so far are below it.</summary>
    public ulong NextSendSequenceNumber => Volatile.Read(ref _nextSendSequenceNumber);

    /// <inheritdoc/>
    /// <remarks>It is <c>udp</c>.</remarks>
    public string ChannelId => "udp";

    /// <inheritdoc/>
    /// <remarks>It is <see cref="PacketChannelState.Connected"/> until it is disposed.</remarks>
    public PacketChannelState State => IsDisposed ? PacketChannelState.Disposed : PacketChannelState.Connected;

    /// <inheritdoc/>
    public DateTime LastActivityTime => PacketStat.LastActivityTime;

    /// <inheritdoc/>
    public Traffic Traffic => new(PacketStat.SentBytes, PacketStat.ReceivedBytes);

    /// <inheritdoc/>
    /// <remarks>It does nothing: the channel carries packets from its creation.</remarks>
    public void Start() { }

    /// <summary>Initializes a new instance of the <see cref="UdpPacketChannel"/> class.</summary>
    /// <param name="sessionId">The session's ID.</param>
    /// <param name="key">The session's key, which seals and opens its datagrams.</param>
    /// <param name="isServer"><see langword="true"/> for the server's side of the session.</param>
    /// <param name="firstSendSequenceNumber">The sequence number of the channel's first datagram.</param>
    /// <param name="trafficMeter">
    /// The meter that counts the channel's traffic and holds what it receives to its receive limit, or
    /// <see langword="null"/>; a server gives one.
    /// </param>
    /// <param name="isLegacy">
    /// <see langword="true"/> to seal and open the legacy way, for a server's session below
    /// <see cref="UdpDatagram.MinProtocolVersion"/>.
    /// </param>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// <paramref name="key"/> is not an AES key's size.
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">The platform has no AES-GCM.</exception>
    protected UdpPacketChannel(ulong sessionId, ReadOnlySpan<byte> key, bool isServer, ulong firstSendSequenceNumber,
        TrafficMeter? trafficMeter, bool isLegacy)
        : base(new PacketTransportOptions {
            AutoDisposePackets = true,
            Blocking = false
        })
    {
        SessionId = sessionId;
        _isServer = isServer;
        _isLegacy = isLegacy;
        _sendCryptor = new AesGcmCryptor(key, UdpDatagram.TagLength);
        _receiveCryptor = new AesGcmCryptor(key, UdpDatagram.TagLength);
        _nextSendSequenceNumber = firstSendSequenceNumber;
        _trafficMeter = trafficMeter;
    }

    /// <summary>Gets where the channel's datagrams go now.</summary>
    /// <returns>The route, or <see langword="null"/> for none.</returns>
    /// <remarks>
    /// With none, or with a closed socket, the datagrams are lost, as on any UDP path, yet count as sent in the
    /// channel's stats.
    /// </remarks>
    protected abstract UdpRoute? GetRoute();

    /// <inheritdoc/>
    /// <remarks>
    /// The packets go in as few datagrams as they fit, each sealed in place in the one buffer the loop reuses: up to
    /// 1,380 bytes of packets a datagram, or one larger packet alone. A packet larger than the tunnel's largest is
    /// dropped and logged.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The traffic meter is disposed.</exception>
    /// <exception cref="Exception">The socket sent less than a whole datagram.</exception>
    protected override async ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        var route = GetRoute();
        if (route is not { Transmitter.Connected: true })
            return;

        var buffer = _sendBuffer ??= new byte[UdpDatagram.HeaderLength + MaxPayloadLength];
        var payloadLength = 0;

        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var packetBytes = ipPackets[i].Buffer;
            if (packetBytes.Length > MaxPayloadLength) {
                VhLogger.Instance.LogWarning(GeneralEventId.Udp,
                    "Packet is too big to send. PacketLength: {PacketLength}", packetBytes.Length);
                continue;
            }

            if (payloadLength > 0 && payloadLength + packetBytes.Length > MaxBatchLength) {
                await SendDatagramAsync(route, buffer, payloadLength).Vhc();
                payloadLength = 0;
            }

            packetBytes.Span.CopyTo(buffer.AsSpan(UdpDatagram.HeaderLength + payloadLength));
            payloadLength += packetBytes.Length;
        }

        if (payloadLength > 0)
            await SendDatagramAsync(route, buffer, payloadLength).Vhc();
    }

    private async ValueTask SendDatagramAsync(UdpRoute route, byte[] buffer, int payloadLength)
    {
        var length = UdpDatagram.Seal(_sendCryptor, SessionId, _nextSendSequenceNumber, isServerSent: _isServer,
            _isLegacy, buffer, payloadLength);
        Volatile.Write(ref _nextSendSequenceNumber, _nextSendSequenceNumber + 1);

        await route.Transmitter.SendAsync(buffer.AsMemory(0, length), route.EndPoint).Vhc();
        _trafficMeter?.OnSent(payloadLength);
    }

    /// <summary>Opens a datagram of the session's in place.</summary>
    /// <param name="datagram">The datagram, at least a header long.</param>
    /// <returns>
    /// Its sequence number, or <see langword="null"/> for a copy of one opened before, or one older than the replay
    /// window, whose packets must not go up again.
    /// </returns>
    /// <remarks>Not thread-safe, as the key's context and the window are not: its callers serialize it.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="datagram"/> is shorter than a header.</exception>
    /// <exception cref="System.Security.Cryptography.AuthenticationTagMismatchException">
    /// The session's peer did not seal the datagram; nothing has read it, and the window is as it was.
    /// </exception>
    protected ulong? Open(Span<byte> datagram)
    {
        var sequenceNumber = UdpDatagram.Open(_receiveCryptor, SessionId, isServerSent: !_isServer, _isLegacy,
            datagram);
        _replayWindow ??= new ReplayWindow();
        return _replayWindow.TryAdd(sequenceNumber) ? sequenceNumber : null;
    }

    /// <summary>Hands an opened payload's packets up to the tunnel, which keep the received array.</summary>
    /// <param name="payload">The opened payload: packets back to back.</param>
    /// <remarks>
    /// UDP has no backpressure, so a packet past the meter's receive limit is dropped, never waited on: on a server, one
    /// read loop serves every session. A packet message is dropped, as one is a channel's own and UDP has none.
    /// </remarks>
    /// <exception cref="ArgumentException">A packet is broken; the rest of the payload is lost.</exception>
    /// <exception cref="NotSupportedException">A packet is neither IPv4 nor IPv6; the rest of the payload is lost.</exception>
    /// <exception cref="ObjectDisposedException">The traffic meter is disposed.</exception>
    // left so, as a forged datagram costs the read loop as much
    protected void HandUp(Memory<byte> payload)
    {
        var offset = 0;
        while (offset < payload.Length) {
            var packetLength = PacketUtil.ReadPacketLength(payload.Span[offset..]);
            var ipPacket = PacketBuilder.Attach(payload.Slice(offset, packetLength));
            offset += packetLength;

            if (_trafficMeter != null && _trafficMeter.ShouldThrottleReceive(ipPacket.PacketLength)) {
                PacketLogger.LogPacket(ipPacket, "Dropping a UDP packet due to receive throttle.",
                    eventId: GeneralEventId.Udp);
                ipPacket.Dispose();
                continue;
            }

            _trafficMeter?.OnReceived(ipPacket.PacketLength);

            // a packet message is a channel's own, and UDP has no channel
            if (PacketMessageHandler.IsPacketMessage(ipPacket)) {
                ipPacket.Dispose();
                continue;
            }

            OnPacketReceived(ipPacket);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It disposes the keys: a send or an open still running meets a disposed key and throws, rather than using a freed
    /// one.
    /// </remarks>
    protected override void DisposeManaged()
    {
        _sendCryptor.Dispose();
        _receiveCryptor.Dispose();
        base.DisposeManaged();
    }

    /// <inheritdoc/>
    /// <remarks>It disposes the channel at once: it has no connection to hand back.</remarks>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

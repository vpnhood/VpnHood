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

// A session's UDP, for the session's whole life: its keys, its send count and a send queue of its own. Its one send
// loop packs the queued packets into datagrams and seals each with the next seq, so the count and the send key need no
// lock. Its tunnel sends on it while UDP is on; what it receives goes up to the tunnel, on or off
public abstract class UdpPacketChannel : PacketTransport, IPacketChannel
{
    // what a datagram carries at most: the packets fit the tunnel's MTU, which leaves room for the overhead
    private const int MaxPayloadLength = TransportDefaults.MaxPacketSize - TransportDefaults.MtuOverhead;

    private readonly bool _isServer;
    private readonly AesGcmCryptor _sendCryptor;
    private readonly AesGcmCryptor _receiveCryptor;
    private readonly TrafficMeter? _trafficMeter;
    private ulong _nextSendSequenceNumber;
    private byte[]? _sendBuffer;
    private ReplayWindow? _replayWindow; // made with the first datagram, so a session on TCP holds none

    public ulong SessionId { get; }

    // the seq of its next datagram: the ones sent so far are below it
    public ulong NextSendSequenceNumber => Volatile.Read(ref _nextSendSequenceNumber);

    // A channel with no connection: one per session, carrying packets from its creation until it is disposed, so it
    // has nothing to start and no close of its own. Its tunnel holds it beside the channel manager, never in it
    public string ChannelId => "udp";
    public PacketChannelState State => IsDisposed ? PacketChannelState.Disposed : PacketChannelState.Connected;
    public DateTime LastActivityTime => PacketStat.LastActivityTime;
    public Traffic Traffic => new(PacketStat.SentBytes, PacketStat.ReceivedBytes);
    public void Start() { }

    protected UdpPacketChannel(ulong sessionId, ReadOnlySpan<byte> key, bool isServer, ulong firstSendSequenceNumber,
        TrafficMeter? trafficMeter)
        : base(new PacketTransportOptions {
            AutoDisposePackets = true,
            Blocking = false
        })
    {
        SessionId = sessionId;
        _isServer = isServer;
        _sendCryptor = new AesGcmCryptor(key, UdpDatagram.TagLength);
        _receiveCryptor = new AesGcmCryptor(key, UdpDatagram.TagLength);
        _nextSendSequenceNumber = firstSendSequenceNumber;
        _trafficMeter = trafficMeter;
    }

    // where its datagrams go now; with none, or with a closed socket, they are lost, as on any UDP path
    protected abstract UdpRoute? GetRoute();

    // the packets go in as few datagrams as they fit, each sealed in place in the one buffer the loop reuses
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

            if (payloadLength + packetBytes.Length > MaxPayloadLength) {
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
            buffer, payloadLength);
        Volatile.Write(ref _nextSendSequenceNumber, _nextSendSequenceNumber + 1);

        await route.Transmitter.SendAsync(buffer.AsMemory(0, length), route.EndPoint).Vhc();
        _trafficMeter?.OnSent(payloadLength);
    }

    // Opens a datagram in place and returns its seq, or null for a copy of one opened before, or one older than the
    // replay window, whose packets must not go up again. A forgery throws AuthenticationTagMismatchException before
    // anything reads it, and leaves the window as it was. Not thread-safe, as the key's context and the window are
    // not: its callers serialize it
    protected ulong? Open(Span<byte> datagram)
    {
        var sequenceNumber = UdpDatagram.Open(_receiveCryptor, SessionId, isServerSent: !_isServer, datagram);
        _replayWindow ??= new ReplayWindow();
        return _replayWindow.TryAdd(sequenceNumber) ? sequenceNumber : null;
    }

    // The opened payload's packets go up to the tunnel, keeping the received array, which is theirs. UDP has no
    // backpressure, so a packet past the receive limit is dropped, never waited on: on a server, one read loop serves
    // every session. Only a server limits what it receives. A broken packet throws, losing the rest of its datagram:
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

    // a send or an open still running meets a disposed key and throws, rather than using a freed one
    protected override void DisposeManaged()
    {
        _sendCryptor.Dispose();
        _receiveCryptor.Dispose();
        base.DisposeManaged();
    }

    // nothing to wait for: it has no connection to hand back
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

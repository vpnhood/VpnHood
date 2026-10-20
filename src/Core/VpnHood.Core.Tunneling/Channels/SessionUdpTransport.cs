using System.Net;
using VpnHood.Core.Tunneling.Cryptography;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Channels;

// A session's UDP keys, send count and route. A server's moves to the listener and the endpoint of the newest datagram
// it has authenticated, so a forged datagram, or a copy of an older one, moves nothing
public class SessionUdpTransport(
    UdpChannelTransmitter channelTransmitter,
    ulong sessionId,
    ReadOnlySpan<byte> key,
    IPEndPoint? remoteEndPoint,
    bool isServer,
    ulong firstSendSequenceNumber)
    : IUdpTransport
{
    // its own count, taken under its transmitter's send lock: a nonce must never repeat under the session's key
    private ulong _nextSendSequenceNumber = firstSendSequenceNumber;

    // the receive AesGcm is not thread-safe, and two listeners may bring the session's datagrams at once
    private readonly Lock _receiveLock = new();
    private readonly AesGcmCryptor _receiveCryptor = new(key, UdpChannelTransmitter.TagLength);
    private ulong _lastReceivedSequenceNumber;

    public bool IsServer { get; } = isServer;
    internal ulong SessionId { get; } = sessionId;
    public IPEndPoint? RemoteEndPoint { get; private set; } = remoteEndPoint;
    internal ICryptor SendCryptor { get; } = new AesGcmCryptor(key, UdpChannelTransmitter.TagLength);
    public UdpChannelTransmitter ChannelTransmitter { get; private set; } = channelTransmitter;
    public Action<Memory<byte>>? DataReceived { get; set; }
    public int OverheadLength => TransportDefaults.MtuOverhead;
    public bool Connected => ChannelTransmitter.Connected;

    // read once its transmitter is disposed, for the session's next transport to count on from
    public ulong NextSendSequenceNumber => Volatile.Read(ref _nextSendSequenceNumber);

    internal ulong TakeSendSequenceNumber() => _nextSendSequenceNumber++;

    // Decrypts the datagram into plainText; a bad tag throws, and nothing has changed. True for the newest datagram a
    // server's transport has authenticated, which moves it to the transmitter and the endpoint it came from
    internal bool Receive(UdpChannelTransmitter transmitter, ReadOnlySpan<byte> datagram, ulong seq, Span<byte> nonce,
        Span<byte> plainText, IPEndPoint remoteEndPoint)
    {
        lock (_receiveLock) {
            UdpChannelTransmitter.DecryptDatagram(_receiveCryptor, datagram, SessionId, seq, isServerSent: !IsServer,
                nonce, plainText);
            if (!IsServer || seq <= _lastReceivedSequenceNumber)
                return false;

            _lastReceivedSequenceNumber = seq;
            ChannelTransmitter = transmitter;
            RemoteEndPoint = remoteEndPoint;
            return true;
        }
    }

    public Task SendAsync(ReadOnlyMemory<byte> buffer)
    {
        return RemoteEndPoint is { } endPoint
            ? ChannelTransmitter.SendAsync(this, endPoint, buffer)
            : throw new InvalidOperationException("RemoteEndPoint is not set.");
    }

    public void Dispose()
    {
        SendCryptor.Dispose();
        _receiveCryptor.Dispose();
    }
}

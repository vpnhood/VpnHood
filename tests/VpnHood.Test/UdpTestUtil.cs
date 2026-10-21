using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using VpnHood.Core.Client;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test;

internal static class UdpTestUtil
{
    // below 2⁶³, as a client's, so a count past it never wraps
    public static ulong GetRandomSequenceNumber()
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8)) >> 1;
    }

    // A client's side of a session, its socket open as a session opens it, its count from the given seq. The socket's
    // announcement takes that seq, in a datagram of its own, gone before this returns: a test's packets go in the next
    public static ClientUdpPacketChannel CreateClientChannel(IPEndPoint serverEndPoint, ulong sessionId,
        byte[] sessionKey, ulong? firstSendSequenceNumber = null)
    {
        var channel = new ClientUdpPacketChannel(new BindingSocketFactory(new SystemSocketFactory()), serverEndPoint,
            bufferSize: null, sessionId, sessionKey, firstSendSequenceNumber ?? GetRandomSequenceNumber(),
            new TrafficMeter());
        channel.OpenSocket();
        Assert.IsTrue(SpinWait.SpinUntil(() => !channel.IsSending, TimeSpan.FromSeconds(5)),
            "The socket's announcement must go out.");
        return channel;
    }

    // what a forger can send: the session's id in clear, any seq, a random tag
    public static byte[] CreateForgedDatagram(ulong sessionId)
    {
        var datagram = new byte[UdpDatagram.HeaderLength];
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(1, 8), sessionId);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(9, 8), GetRandomSequenceNumber());
        RandomNumberGenerator.Fill(datagram.AsSpan(17, UdpDatagram.TagLength));
        return datagram;
    }

    // Compatibility (2026-10): an old client's datagram, below protocol 14, sealed as it seals one: the salt as it is,
    // in either direction
    public static byte[] SealLegacyDatagram(ulong sessionId, byte[] sessionKey, ulong seq, ReadOnlySpan<byte> payload)
    {
        var datagram = new byte[UdpDatagram.HeaderLength + payload.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(1, 8), sessionId);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(9, 8), seq);
        using var aesGcm = new AesGcm(sessionKey, UdpDatagram.TagLength);
        aesGcm.Encrypt(BuildLegacyNonce(sessionId, seq), payload, datagram.AsSpan(UdpDatagram.HeaderLength),
            datagram.AsSpan(17, UdpDatagram.TagLength), datagram.AsSpan(1, 16));
        return datagram;
    }

    // the payload of a datagram the server sealed for an old client, opened as that client opens it
    public static byte[] OpenLegacyDatagram(byte[] sessionKey, byte[] datagram)
    {
        var sessionId = UdpDatagram.ReadSessionId(datagram);
        var seq = BinaryPrimitives.ReadUInt64LittleEndian(datagram.AsSpan(9, 8));
        var payload = new byte[datagram.Length - UdpDatagram.HeaderLength];
        using var aesGcm = new AesGcm(sessionKey, UdpDatagram.TagLength);
        aesGcm.Decrypt(BuildLegacyNonce(sessionId, seq), datagram.AsSpan(UdpDatagram.HeaderLength),
            datagram.AsSpan(17, UdpDatagram.TagLength), payload, datagram.AsSpan(1, 16));
        return payload;
    }

    private static byte[] BuildLegacyNonce(ulong sessionId, ulong seq)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(0, 8), seq);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(8, 4), (uint)(sessionId ^ (sessionId >> 32)));
        return nonce;
    }

    public static async Task AssertNothingReceived(UdpClient udpClient, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await udpClient.ReceiveAsync(timeoutCts.Token),
            "Nothing more may arrive.");
    }
}

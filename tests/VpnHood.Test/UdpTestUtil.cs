using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Test;

internal static class UdpTestUtil
{
    // below 2⁶³, as a client's, so a count past it never wraps
    public static ulong GetRandomSequenceNumber()
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8)) >> 1;
    }

    // what a forger can send: the session's id in clear, any seq, a random tag
    public static byte[] CreateForgedDatagram(ulong sessionId)
    {
        var datagram = new byte[UdpChannelTransmitter.HeaderLength];
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(1, 8), sessionId);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(9, 8), GetRandomSequenceNumber());
        RandomNumberGenerator.Fill(datagram.AsSpan(17, UdpChannelTransmitter.TagLength));
        return datagram;
    }

    public static async Task AssertNothingReceived(UdpClient udpClient, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await udpClient.ReceiveAsync(timeoutCts.Token),
            "Nothing may reach the forger.");
    }
}

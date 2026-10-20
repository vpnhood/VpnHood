using System.Buffers.Binary;
using VpnHood.Core.Tunneling.Cryptography;

namespace VpnHood.Core.Tunneling.Channels;

// A session's UDP datagram: version(1) | sessionId(8) | seq(8) | tag(16) | ciphertext, sealed with AES-GCM under the
// session's key, with sessionId|seq as its associated data. The version byte is written as 0 and not read
public static class UdpDatagram
{
    // the first protocol of this scheme, with the server-reply bit and the freshness mark: a session below it has no
    // UDP, from either side
    public const int MinProtocolVersion = 14;

    private const int SessionIdOffset = 1;
    private const int SeqOffset = SessionIdOffset + 8;
    private const int TagOffset = SeqOffset + 8;
    private const int AssociatedDataLength = 16; // sessionId | seq
    public const int TagLength = 16;
    public const int HeaderLength = TagOffset + TagLength;

    public static ulong ReadSessionId(ReadOnlySpan<byte> datagram)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(datagram.Slice(SessionIdOffset, 8));
    }

    // seals in place the payload that follows the header, and returns the datagram's length
    internal static int Seal(ICryptor cryptor, ulong sessionId, ulong seq, bool isServerSent, Span<byte> buffer,
        int payloadLength)
    {
        buffer[0] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(SessionIdOffset, 8), sessionId);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(SeqOffset, 8), seq);

        Span<byte> nonce = stackalloc byte[12];
        BuildNonce(nonce, sessionId, seq, isServerSent);
        var payload = buffer.Slice(HeaderLength, payloadLength);
        cryptor.Encrypt(nonce, payload, payload, buffer.Slice(TagOffset, TagLength),
            buffer.Slice(SessionIdOffset, AssociatedDataLength));

        return HeaderLength + payloadLength;
    }

    // opens in place the payload that follows the header, and returns the datagram's seq. One the key did not seal in
    // that direction throws AuthenticationTagMismatchException, so nothing ever reads a forgery
    internal static ulong Open(ICryptor cryptor, ulong sessionId, bool isServerSent, Span<byte> datagram)
    {
        var seq = BinaryPrimitives.ReadUInt64LittleEndian(datagram.Slice(SeqOffset, 8));
        Span<byte> nonce = stackalloc byte[12];
        BuildNonce(nonce, sessionId, seq, isServerSent);
        var payload = datagram[HeaderLength..];
        cryptor.Decrypt(nonce, payload, datagram.Slice(TagOffset, TagLength), payload,
            datagram.Slice(SessionIdOffset, AssociatedDataLength));

        return seq;
    }

    // nonce = seq(8) | salt(4) from the session id. The salt's top bit, the top of the nonce's last byte, is set for a
    // datagram the server sent: one side's datagram never opens as the other's, and the two never share a nonce under
    // the session's one key
    private static void BuildNonce(Span<byte> nonce, ulong sessionId, ulong seq, bool isServerSent)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[..8], seq);
        var salt = (uint)(sessionId ^ (sessionId >> 32));
        salt = isServerSent ? salt | 0x8000_0000 : salt & 0x7FFF_FFFF;
        BinaryPrimitives.WriteUInt32LittleEndian(nonce.Slice(8, 4), salt);
    }
}

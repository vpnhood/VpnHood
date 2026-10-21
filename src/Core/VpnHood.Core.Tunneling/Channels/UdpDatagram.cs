using System.Buffers.Binary;
using VpnHood.Core.Tunneling.Cryptography;

namespace VpnHood.Core.Tunneling.Channels;

// A session's UDP datagram: version(1) | sessionId(8) | seq(8) | tag(16) | ciphertext, sealed with AES-GCM under the
// session's key, with sessionId|seq as its associated data. The version byte is written as 0 and not read: it is not
// in the associated data, so it must go in there before it may mean anything
public static class UdpDatagram
{
    // the first protocol of this scheme, with the server-reply bit and the freshness mark: a client takes no UDP from a
    // server below it, and a server seals a session below it the legacy way
    public const int MinProtocolVersion = 14;

    // Compatibility (2026-10): the first protocol of the legacy way; a session below it has no UDP, as before protocol
    // 14, since its client speaks an older one. Drop with the legacy way, once MinClientProtocolVersion is 14
    public const int MinLegacyProtocolVersion = 11;

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
    internal static int Seal(ICryptor cryptor, ulong sessionId, ulong seq, bool isServerSent, bool isLegacy,
        Span<byte> buffer, int payloadLength)
    {
        buffer[0] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(SessionIdOffset, 8), sessionId);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(SeqOffset, 8), seq);

        Span<byte> nonce = stackalloc byte[12];
        BuildNonce(nonce, sessionId, seq, isServerSent, isLegacy);
        var payload = buffer.Slice(HeaderLength, payloadLength);
        cryptor.Encrypt(nonce, payload, payload, buffer.Slice(TagOffset, TagLength),
            buffer.Slice(SessionIdOffset, AssociatedDataLength));

        return HeaderLength + payloadLength;
    }

    // opens in place the payload that follows the header, and returns the datagram's seq. One the key did not seal in
    // that direction throws AuthenticationTagMismatchException, so nothing ever reads a forgery
    internal static ulong Open(ICryptor cryptor, ulong sessionId, bool isServerSent, bool isLegacy,
        Span<byte> datagram)
    {
        var seq = BinaryPrimitives.ReadUInt64LittleEndian(datagram.Slice(SeqOffset, 8));
        Span<byte> nonce = stackalloc byte[12];
        BuildNonce(nonce, sessionId, seq, isServerSent, isLegacy);
        var payload = datagram[HeaderLength..];
        cryptor.Decrypt(nonce, payload, datagram.Slice(TagOffset, TagLength), payload,
            datagram.Slice(SessionIdOffset, AssociatedDataLength));

        return seq;
    }

    // nonce = seq(8) | salt(4) from the session id. The salt's top bit, the top of the nonce's last byte, is set for a
    // datagram the server sent: one side's datagram never opens as the other's, and the two never share a nonce under
    // the session's one key.
    // Compatibility (2026-10): a legacy session's, below protocol 14, keeps the salt as it is, so the two sides share
    // nonces; drop once MinClientProtocolVersion is 14 and the sessions from before it have ended
    private static void BuildNonce(Span<byte> nonce, ulong sessionId, ulong seq, bool isServerSent, bool isLegacy)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[..8], seq);
        var salt = (uint)(sessionId ^ (sessionId >> 32));
        if (!isLegacy)
            salt = isServerSent ? salt | 0x8000_0000 : salt & 0x7FFF_FFFF;
        BinaryPrimitives.WriteUInt32LittleEndian(nonce.Slice(8, 4), salt);
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace VpnHood.Test.Providers;

// The first packets of a QUIC version 1 connection, made as a client makes them (RFC 9001, 5), so a test of the name
// reader needs no QUIC library on the machine, and can make what no library sends: a ClientHello in any number of
// datagrams, one that is not well formed, a packet of any connection ID
public sealed class TestQuicClient
{
    // a datagram that carries an Initial is this long at least (RFC 9000, 14.1)
    private const int InitialDatagramLength = 1200;
    private const int PacketNumberLength = 2;
    private const int TagLength = 16;

    private static readonly byte[] InitialSalt = [
        0x38, 0x76, 0x2c, 0xf7, 0xf5, 0x59, 0x34, 0xb3, 0x4d, 0x17, 0x9a, 0xe6, 0xa4, 0xc8, 0x0c, 0xad, 0xcc, 0xbb,
        0x7f, 0x0a
    ];

    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly byte[] _headerProtectionKey;
    private ushort _packetNumber;

    public byte[] DestinationConnectionId { get; }

    public TestQuicClient(byte[]? destinationConnectionId = null)
    {
        DestinationConnectionId = destinationConnectionId ?? RandomNumberGenerator.GetBytes(8);

        // the client's Initial keys, from the connection ID alone
        var initialSecret = HKDF.Extract(HashAlgorithmName.SHA256, DestinationConnectionId, InitialSalt);
        var clientSecret = ExpandLabel(initialSecret, "client in", 32);
        _key = ExpandLabel(clientSecret, "quic key", 16);
        _iv = ExpandLabel(clientSecret, "quic iv", 12);
        _headerProtectionKey = ExpandLabel(clientSecret, "quic hp", 16);
    }

    // The Initial datagrams that carry a handshake message, in as many datagrams as asked, a part of it in each
    public List<byte[]> BuildInitials(byte[] handshake, int datagramCount = 1)
    {
        var datagrams = new List<byte[]>();
        for (var i = 0; i < datagramCount; i++) {
            var start = handshake.Length * i / datagramCount;
            var end = handshake.Length * (i + 1) / datagramCount;
            datagrams.Add(BuildInitial(handshake.AsSpan(start, end - start), start));
        }

        return datagrams;
    }

    // An Initial datagram that carries a part of the handshake, from the given offset of it; no part, for an Initial
    // that carries padding alone
    public byte[] BuildInitial(ReadOnlySpan<byte> handshakePart, int handshakeOffset = 0)
    {
        // the header up to the packet number: a long header of an Initial, the version, the destination connection
        // ID, no source connection ID, no token, and the length of what follows
        var datagram = new byte[InitialDatagramLength];
        var position = 0;
        datagram[position++] = 0xC0 | (PacketNumberLength - 1);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(position), 1);
        position += 4;
        datagram[position++] = (byte)DestinationConnectionId.Length;
        DestinationConnectionId.CopyTo(datagram.AsSpan(position));
        position += DestinationConnectionId.Length;
        datagram[position++] = 0;
        datagram[position++] = 0;
        var payloadLength = InitialDatagramLength - (position + 2 + PacketNumberLength) - TagLength;
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(position),
            (ushort)(0x4000 | (PacketNumberLength + payloadLength + TagLength)));
        position += 2;
        var packetNumberOffset = position;
        var packetNumber = _packetNumber++;
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(position), packetNumber);
        position += PacketNumberLength;

        // the frames: the handshake's part in a CRYPTO frame, and padding to the datagram's length
        var payload = new byte[payloadLength];
        if (!handshakePart.IsEmpty) {
            var frame = 0;
            payload[frame++] = 0x06;
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(frame), (ushort)(0x4000 | handshakeOffset));
            frame += 2;
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(frame), (ushort)(0x4000 | handshakePart.Length));
            frame += 2;
            handshakePart.CopyTo(payload.AsSpan(frame));
        }

        // the payload's protection, with the header as it is before its own
        var nonce = _iv.ToArray();
        nonce[^2] ^= (byte)(packetNumber >> 8);
        nonce[^1] ^= (byte)packetNumber;
        using (var aesGcm = new AesGcm(_key, TagLength))
            aesGcm.Encrypt(nonce, payload, datagram.AsSpan(position, payloadLength),
                datagram.AsSpan(position + payloadLength, TagLength), datagram.AsSpan(0, position));

        // the header's protection: the first byte's low bits and the packet number, by a sample of the payload
        using var aes = Aes.Create();
        aes.Key = _headerProtectionKey;
        var mask = aes.EncryptEcb(datagram.AsSpan(packetNumberOffset + 4, 16), PaddingMode.None);
        datagram[0] ^= (byte)(mask[0] & 0x0F);
        for (var i = 0; i < PacketNumberLength; i++)
            datagram[packetNumberOffset + i] ^= mask[1 + i];

        return datagram;
    }

    // A datagram of the connection's early data: a long header of a 0-RTT packet with the connection's ID, over bytes
    // that no one here can open, or needs to
    public byte[] BuildEarlyData()
    {
        return BuildEarlyData(DestinationConnectionId);
    }

    public static byte[] BuildEarlyData(ReadOnlySpan<byte> destinationConnectionId)
    {
        var datagram = new byte[200];
        RandomNumberGenerator.Fill(datagram);
        var position = 0;
        datagram[position++] = 0xD0 | (PacketNumberLength - 1);
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(position), 1);
        position += 4;
        datagram[position++] = (byte)destinationConnectionId.Length;
        destinationConnectionId.CopyTo(datagram.AsSpan(position));
        position += destinationConnectionId.Length;
        datagram[position++] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(position),
            (ushort)(0x4000 | (datagram.Length - position - 2)));
        return datagram;
    }

    // a packet of a connection past its handshake: a short header, which names no connection a reader can tell
    public static byte[] BuildShortHeaderDatagram()
    {
        var datagram = new byte[64];
        datagram[0] = 0x40;
        return datagram;
    }

    private static byte[] ExpandLabel(byte[] secret, string label, int length)
    {
        var labelBytes = Encoding.ASCII.GetBytes("tls13 " + label);
        byte[] info = [(byte)(length >> 8), (byte)length, (byte)labelBytes.Length, .. labelBytes, 0];
        return HKDF.Expand(HashAlgorithmName.SHA256, secret, length, info);
    }
}

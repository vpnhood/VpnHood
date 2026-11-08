using System.Buffers.Binary;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

/// <summary>
/// Represents an ICMPv6 message (RFC 4443) over a buffer, which it reads and writes in place.
/// </summary>
/// <remarks>
/// Changing a field does not update the checksum: call <see cref="UpdateChecksum"/> before sending.
/// </remarks>
public class IcmpV6Packet : IChecksumPayloadPacket
{
    private readonly Memory<byte> _buffer;

    /// <inheritdoc/>
    public Memory<byte> Buffer => _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IcmpV6Packet"/> class over a buffer, to parse a message or to
    /// build one.
    /// </summary>
    /// <param name="buffer">The message's bytes, header and payload, from 8 to 65535 bytes.</param>
    /// <param name="building">
    /// <see langword="true"/> to build: the 8-byte header is cleared and the payload is left as it is.
    /// <see langword="false"/> to parse the bytes as they are.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> is shorter than 8 bytes or longer than 65535.</exception>
    public IcmpV6Packet(Memory<byte> buffer, bool building)
    {
        // Base ICMPv6 header requires at least 8 bytes, but Echo messages require 12 bytes minimum
        if (buffer.Length < 8)
            throw new ArgumentException("Buffer too small for ICMPv6 header.");

        if (buffer.Length > 0xFFFF)
            throw new ArgumentException("Buffer too large for ICMPv6 packet.");

        if (building)
            buffer.Span[..8].Clear(); // the header only: the IP packet it is built in was cleared whole

        _buffer = buffer;
    }

    /// <summary>
    /// Gets or sets the message type.
    /// </summary>
    public IcmpV6Type Type {
        get => (IcmpV6Type)_buffer.Span[0];
        set => _buffer.Span[0] = (byte)value;
    }

    /// <summary>
    /// Gets or sets the message code, whose meaning depends on <see cref="Type"/>; see <see cref="IcmpV6Code"/>.
    /// </summary>
    public byte Code {
        get => _buffer.Span[1];
        set => _buffer.Span[1] = value;
    }

    /// <summary>
    /// Gets or sets the checksum field as it is in the header.
    /// </summary>
    public ushort Checksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(2, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(2, 2), value);
    }

    /// <summary>
    /// Gets or sets the 4 bytes after the checksum, whose meaning depends on <see cref="Type"/>: an echo's
    /// identifier and sequence number, the MTU of a "packet too big" message, or a parameter problem's pointer.
    /// </summary>
    public uint MessageSpecific {
        get => BinaryPrimitives.ReadUInt32BigEndian(_buffer.Span.Slice(4, 4));
        set => BinaryPrimitives.WriteUInt32BigEndian(_buffer.Span.Slice(4, 4), value);
    }

    /// <summary>
    /// Gets or sets the identifier of an echo request or reply.
    /// </summary>
    /// <remarks>
    /// For another type, the getter returns 0.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Set on a message that is not an echo request or reply.</exception>
    public ushort Identifier {
        get => IsEcho ? BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(4, 2)) : (ushort)0;
        set {
            if (!IsEcho)
                throw new InvalidOperationException("Identifier is only valid for Echo messages.");
            BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(4, 2), value);
        }
    }

    /// <summary>
    /// Gets or sets the sequence number of an echo request or reply.
    /// </summary>
    /// <remarks>
    /// For another type, the getter returns 0.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Set on a message that is not an echo request or reply.</exception>
    public ushort SequenceNumber {
        get => IsEcho ? BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(6, 2)) : (ushort)0;
        set {
            if (!IsEcho)
                throw new InvalidOperationException("SequenceNumber is only valid for Echo messages.");
            BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(6, 2), value);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the message is an echo request or an echo reply.
    /// </summary>
    public bool IsEcho => Type is IcmpV6Type.EchoRequest or IcmpV6Type.EchoReply;

    /// <summary>
    /// Gets the message's payload: the bytes after the 8-byte header, such as an echo's data or the start of the
    /// packet an error is about.
    /// </summary>
    public Memory<byte> Payload => _buffer[8..];

    /// <summary>
    /// Checks the checksum field against the message and the IPv6 pseudo-header.
    /// </summary>
    /// <param name="sourceAddress">The source address of the IP packet, 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, 16 bytes.</param>
    /// <returns><see langword="true"/> if the checksum is valid; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">The addresses are not both 4 or both 16 bytes long.</exception>
    // with its checksum, a valid message sums to all ones, whose complement is zero, whichever zero the field holds
    public bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress) =>
        PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)IpProtocol.IcmpV6, _buffer.Span) == 0;

    /// <summary>
    /// Computes the checksum and writes it to <see cref="Checksum"/>.
    /// </summary>
    /// <param name="sourceAddress">The source address of the IP packet, 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, 16 bytes.</param>
    /// <exception cref="ArgumentException">The addresses are not both 4 or both 16 bytes long.</exception>
    public void UpdateChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress) =>
        Checksum = ComputeChecksum(sourceAddress, destinationAddress);

    /// <summary>
    /// Computes the checksum the message calls for, without writing it.
    /// </summary>
    /// <remarks>
    /// The checksum field is zero while the sum runs and is restored after, so no other thread may use the packet
    /// meanwhile.
    /// </remarks>
    /// <param name="sourceAddress">The source address of the IP packet, 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, 16 bytes.</param>
    /// <returns>The checksum, as a number to write in network byte order.</returns>
    /// <exception cref="ArgumentException">The addresses are not both 4 or both 16 bytes long.</exception>
    public ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        var orgChecksum = Checksum;
        Checksum = 0;

        try {
            return PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)IpProtocol.IcmpV6, _buffer.Span);
        }
        finally {
            Checksum = orgChecksum;
        }
    }

    /// <summary>
    /// Returns a one-line description of the message for logs: its type, code and payload length, with an echo's
    /// identifier and sequence number.
    /// </summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        var str = $"ICMPv6 Packet: Type={Type}, Code={Code}, PayloadLen={Payload.Length}";
        if (!IsEcho)
            str += $", MsgSpec={MessageSpecific}";
        else
            str += $", Id={Identifier}, Seq={SequenceNumber}";
        return str;
    }
}

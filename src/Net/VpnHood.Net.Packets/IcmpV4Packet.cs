using System.Buffers.Binary;

namespace VpnHood.Net.Packets;

/// <summary>
/// Represents an ICMPv4 message (RFC 792) over a buffer, which it reads and writes in place.
/// </summary>
/// <remarks>
/// Changing a field does not update the checksum: call <see cref="UpdateChecksum"/> before sending.
/// </remarks>
public class IcmpV4Packet : IChecksumPayloadPacket
{
    private readonly Memory<byte> _buffer;

    /// <inheritdoc/>
    public Memory<byte> Buffer => _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IcmpV4Packet"/> class over a buffer, to parse a message or to
    /// build one.
    /// </summary>
    /// <param name="buffer">The message's bytes, header and payload, from 8 to 65535 bytes.</param>
    /// <param name="building">
    /// <see langword="true"/> to build: the 8-byte header is cleared and the payload is left as it is.
    /// <see langword="false"/> to parse the bytes as they are.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> is shorter than 8 bytes or longer than 65535.</exception>
    public IcmpV4Packet(Memory<byte> buffer, bool building)
    {
        // Base ICMPv4 header requires at least 8 bytes, but Echo messages require 8 bytes minimum
        if (buffer.Length < 8)
            throw new ArgumentException("Buffer too small for ICMPv4 header.");

        if (buffer.Length > 0xFFFF)
            throw new ArgumentException("Buffer too large for ICMPv4 packet.");

        if (building)
            buffer.Span[..8].Clear(); // the header only: the IP packet it is built in was cleared whole

        _buffer = buffer;
    }

    /// <summary>
    /// Gets or sets the message type.
    /// </summary>
    public IcmpV4Type Type {
        get => (IcmpV4Type)_buffer.Span[0];
        set => _buffer.Span[0] = (byte)value;
    }

    /// <summary>
    /// Gets or sets the message code, whose meaning depends on <see cref="Type"/>; see <see cref="IcmpV4Code"/>.
    /// </summary>
    public byte Code {
        get => _buffer.Span[1];
        set => _buffer.Span[1] = value;
    }

    /// <summary>
    /// Gets or sets the 4 bytes after the checksum, whose meaning depends on <see cref="Type"/>: an echo's
    /// identifier and sequence number, or the next-hop MTU of a "fragmentation needed" message, in its low 16 bits.
    /// </summary>
    public uint MessageSpecific {
        get => BinaryPrimitives.ReadUInt32BigEndian(_buffer.Span.Slice(4, 4));
        set => BinaryPrimitives.WriteUInt32BigEndian(_buffer.Span.Slice(4, 4), value);
    }

    /// <summary>
    /// Gets or sets the checksum field as it is in the header.
    /// </summary>
    public ushort Checksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(2, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(2, 2), value);
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
    public bool IsEcho => Type is IcmpV4Type.EchoRequest or IcmpV4Type.EchoReply;

    /// <summary>
    /// Gets the message's payload: the bytes after the 8-byte header, such as an echo's data or the start of the
    /// packet an error is about.
    /// </summary>
    public Memory<byte> Payload => _buffer[8..];

    /// <summary>
    /// Checks the checksum field against the message.
    /// </summary>
    /// <param name="sourceAddress">Ignored: ICMPv4 sums no pseudo-header.</param>
    /// <param name="destinationAddress">Ignored: ICMPv4 sums no pseudo-header.</param>
    /// <returns><see langword="true"/> if the checksum is valid; otherwise, <see langword="false"/>.</returns>
    // with its checksum, a valid message sums to all ones, whose complement is zero, whichever zero the field holds
    public bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress) =>
        ChecksumUtils.ComputeChecksum(_buffer.Span) == 0;

    /// <summary>
    /// Computes the checksum and writes it to <see cref="Checksum"/>.
    /// </summary>
    /// <param name="sourceAddress">Ignored: ICMPv4 sums no pseudo-header.</param>
    /// <param name="destinationAddress">Ignored: ICMPv4 sums no pseudo-header.</param>
    public void UpdateChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress) =>
        Checksum = ComputeChecksum(sourceAddress, destinationAddress);

    /// <summary>
    /// Computes the checksum the message calls for, without writing it.
    /// </summary>
    /// <remarks>
    /// The checksum field is zero while the sum runs and is restored after, so no other thread may use the packet
    /// meanwhile.
    /// </remarks>
    /// <param name="sourceAddress">Ignored: ICMPv4 sums no pseudo-header.</param>
    /// <param name="destinationAddress">Ignored: ICMPv4 sums no pseudo-header.</param>
    /// <returns>The checksum, as a number to write in network byte order.</returns>
    public ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        var orgChecksum = Checksum;
        Checksum = 0;

        try {
            var span = _buffer.Span;
            span[2] = 0;
            span[3] = 0;

            return ChecksumUtils.ComputeChecksum(span);
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
        var str = $"ICMPv4 Packet: Type={Type}, Code={Code}, PayloadLen={Payload.Length}";
        if (IsEcho)
            str += $", Id={Identifier}, Seq={SequenceNumber}";
        else
            str += $", MsgSpec={MessageSpecific}";
        return str;
    }
}

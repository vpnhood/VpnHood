using System.Buffers.Binary;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

/// <summary>
/// Represents a TCP segment (RFC 9293) over a buffer, which it reads and writes in place.
/// </summary>
/// <remarks>
/// Changing a field does not update the checksum: call <see cref="UpdateChecksum"/> before sending.
/// </remarks>
public class TcpPacket : IChecksumPayloadPacket
{
    private readonly Memory<byte> _buffer;

    /// <inheritdoc/>
    public Memory<byte> Buffer => _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpPacket"/> class by parsing a segment.
    /// </summary>
    /// <param name="buffer">The segment's bytes, header and payload, exactly as long as the segment.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="buffer"/> is shorter than 20 bytes, or the data offset gives a header shorter than 20 bytes
    /// or longer than the buffer.
    /// </exception>
    public TcpPacket(Memory<byte> buffer)
    {
        if (buffer.Length < 20)
            throw new ArgumentException("Buffer too small for TCP header.", nameof(buffer));

        // the data offset is the header's length: below the 20-byte minimum, Payload would hand header bytes on as data
        var headerLength = (buffer.Span[12] >> 4) * 4;
        if (headerLength < 20 || headerLength > buffer.Length)
            throw new ArgumentException("Invalid TCP data offset.", nameof(buffer));

        _buffer = buffer;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpPacket"/> class by building a segment.
    /// </summary>
    /// <remarks>
    /// The header and its options are cleared and the data offset is set; the payload is left as it is.
    /// </remarks>
    /// <param name="buffer">The segment's bytes, header and payload, exactly as long as the segment.</param>
    /// <param name="optionsLength">The length of the options, a multiple of 4 from 0 to 40.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="optionsLength"/> is out of range or not a multiple of 4.</exception>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> is shorter than the header with its options.</exception>
    public TcpPacket(Memory<byte> buffer, int optionsLength)
    {
        if (optionsLength is < 0 or > 40)
            throw new ArgumentOutOfRangeException(nameof(optionsLength),
                "Options length must be between 0 and 40 bytes.");

        if (optionsLength % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(optionsLength),
                "Options length must be a multiple of 4 bytes.");

        var headerLength = 20 + optionsLength;
        if (buffer.Length < headerLength)
            throw new ArgumentException("Buffer too small for TCP header and its options.", nameof(buffer));

        // the header only: the IP packet it is built in was cleared whole
        buffer.Span[..headerLength].Clear();

        // Set Data Offset (header length in 32-bit words)
        var dataOffset = headerLength / 4;
        buffer.Span[12] = (byte)(dataOffset << 4);

        _buffer = buffer;
    }

    /// <summary>
    /// Gets or sets the source port.
    /// </summary>
    public ushort SourcePort {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span[..2]);
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span[..2], value);
    }

    /// <summary>
    /// Gets or sets the destination port.
    /// </summary>
    public ushort DestinationPort {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(2, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(2, 2), value);
    }

    /// <summary>
    /// Gets or sets the sequence number.
    /// </summary>
    public uint SequenceNumber {
        get => BinaryPrimitives.ReadUInt32BigEndian(_buffer.Span.Slice(4, 4));
        set => BinaryPrimitives.WriteUInt32BigEndian(_buffer.Span.Slice(4, 4), value);
    }

    /// <summary>
    /// Gets or sets the acknowledgment number, which counts only when <see cref="Acknowledgment"/> is set.
    /// </summary>
    public uint AcknowledgmentNumber {
        get => BinaryPrimitives.ReadUInt32BigEndian(_buffer.Span.Slice(8, 4));
        set => BinaryPrimitives.WriteUInt32BigEndian(_buffer.Span.Slice(8, 4), value);
    }

    /// <summary>
    /// Gets or sets the flags byte: CWR, ECE, URG, ACK, PSH, RST, SYN and FIN, from the highest bit.
    /// </summary>
    public byte Flags {
        get => _buffer.Span[13];
        set => _buffer.Span[13] = value;
    }

    private void SetFlag(byte mask, bool value)
    {
        Flags = value ? (byte)(Flags | mask) : (byte)(Flags & ~mask);
    }

    /// <summary>
    /// Gets or sets the URG flag: <see cref="UrgentPointer"/> counts.
    /// </summary>
    public bool Urgent {
        get => (_buffer.Span[13] & 0b0010_0000) != 0;
        set => SetFlag(0b0010_0000, value);
    }

    /// <summary>
    /// Gets or sets the ACK flag: <see cref="AcknowledgmentNumber"/> counts.
    /// </summary>
    public bool Acknowledgment {
        get => (_buffer.Span[13] & 0b0001_0000) != 0;
        set => SetFlag(0b0001_0000, value);
    }

    /// <summary>
    /// Gets or sets the PSH flag: the receiver should pass the data on at once.
    /// </summary>
    public bool Push {
        get => (_buffer.Span[13] & 0b0000_1000) != 0;
        set => SetFlag(0b0000_1000, value);
    }

    /// <summary>
    /// Gets or sets the RST flag: the connection is reset.
    /// </summary>
    public bool Reset {
        get => (_buffer.Span[13] & 0b0000_0100) != 0;
        set => SetFlag(0b0000_0100, value);
    }

    /// <summary>
    /// Gets or sets the SYN flag: the segment opens a connection.
    /// </summary>
    public bool Synchronize {
        get => (_buffer.Span[13] & 0b0000_0010) != 0;
        set => SetFlag(0b0000_0010, value);
    }

    /// <summary>
    /// Gets or sets the FIN flag: the sender has no more data.
    /// </summary>
    public bool Finish {
        get => (_buffer.Span[13] & 0b0000_0001) != 0;
        set => SetFlag(0b0000_0001, value);
    }

    /// <summary>
    /// Gets or sets the window size, in bytes before any window scaling.
    /// </summary>
    public ushort WindowSize {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(14, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(14, 2), value);
    }

    /// <summary>
    /// Gets or sets the checksum field as it is in the header.
    /// </summary>
    public ushort Checksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(16, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(16, 2), value);
    }

    /// <summary>
    /// Gets or sets the urgent pointer, which counts only when <see cref="Urgent"/> is set.
    /// </summary>
    public ushort UrgentPointer {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(18, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(18, 2), value);
    }

    private byte DataOffset => (byte)((_buffer.Span[12] >> 4) * 4);

    /// <summary>
    /// Gets the options: the header's bytes after its fixed 20.
    /// </summary>
    public Memory<byte> Options => _buffer.Slice(20, DataOffset - 20);

    /// <summary>
    /// Gets the segment's payload: the bytes after the header and its options.
    /// </summary>
    public Memory<byte> Payload => _buffer[DataOffset..];

    /// <summary>
    /// Checks the checksum field against the segment and the IP pseudo-header.
    /// </summary>
    /// <param name="sourceAddress">The source address of the IP packet, 4 or 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, of the same length.</param>
    /// <returns><see langword="true"/> if the checksum is valid; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">The addresses are not both 4 or both 16 bytes long.</exception>
    // with its checksum, a valid segment sums to all ones, whose complement is zero, whichever zero the field holds
    public bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        return PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)IpProtocol.Tcp, _buffer.Span) == 0;
    }

    /// <summary>
    /// Computes the checksum and writes it to <see cref="Checksum"/>.
    /// </summary>
    /// <param name="sourceAddress">The source address of the IP packet, 4 or 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, of the same length.</param>
    /// <exception cref="ArgumentException">The addresses are not both 4 or both 16 bytes long.</exception>
    public void UpdateChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        Checksum = ComputeChecksum(sourceAddress, destinationAddress);
    }

    /// <summary>
    /// Computes the checksum the segment calls for, without writing it.
    /// </summary>
    /// <remarks>
    /// The checksum field is zero while the sum runs and is restored after, so no other thread may use the packet
    /// meanwhile.
    /// </remarks>
    /// <param name="sourceAddress">The source address of the IP packet, 4 or 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, of the same length.</param>
    /// <returns>The checksum, as a number to write in network byte order.</returns>
    /// <exception cref="ArgumentException">The addresses are not both 4 or both 16 bytes long.</exception>
    public ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        var orgChecksum = Checksum;
        Checksum = 0;

        try {
            return PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)IpProtocol.Tcp, _buffer.Span);
        }
        finally {
            Checksum = orgChecksum;
        }
    }

    /// <summary>
    /// Returns a one-line description of the segment for logs: its ports, sequence numbers, flags and payload
    /// length.
    /// </summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        return
            $"TCP Packet: SrcPort={SourcePort}, DstPort={DestinationPort}, Seq={SequenceNumber}, Ack={AcknowledgmentNumber}, " +
            $"Flags=[Urgent={Urgent}, Ack={Acknowledgment}, Push={Push}, Reset={Reset}, Sync={Synchronize}, Fin={Finish}], " +
            $"PayloadLen={Payload.Length}";
    }
}

using System.Buffers.Binary;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

/// <summary>
/// Represents a UDP datagram (RFC 768) over a buffer, which it reads and writes in place.
/// </summary>
/// <remarks>
/// Changing a field does not update the checksum: call <see cref="UpdateChecksum"/> before sending.
/// </remarks>
public class UdpPacket : IChecksumPayloadPacket
{
    private readonly Memory<byte> _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpPacket"/> class over a buffer, to parse a datagram or to
    /// build one.
    /// </summary>
    /// <param name="buffer">
    /// The datagram's bytes, header and payload, exactly as long as the datagram: from 8 to 65535 bytes.
    /// </param>
    /// <param name="building">
    /// <see langword="true"/> to build: the 8-byte header is cleared and its length field set to the buffer's
    /// length, and the payload is left as it is. <see langword="false"/> to parse: the length field must match
    /// the buffer's length.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="buffer"/> is shorter than 8 bytes or longer than 65535, or, when parsing, its length differs
    /// from the length field.
    /// </exception>
    public UdpPacket(Memory<byte> buffer, bool building)
    {
        if (buffer.Length < 8)
            throw new ArgumentException("Buffer too small for UDP header.", nameof(buffer));

        // the length field holds at most 65535: a longer datagram would say it is shorter
        if (buffer.Length > 0xFFFF)
            throw new ArgumentException("Buffer too large for the UDP length field.", nameof(buffer));

        // write udpLength
        if (building) {
            buffer.Span[..8].Clear(); // the header only: the IP packet it is built in was cleared whole
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Span.Slice(4, 2), (ushort)buffer.Length);
        }
        else {
            var udpLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Span.Slice(4, 2));
            if (udpLength != buffer.Length)
                throw new ArgumentException("Buffer length does not match UDP length field.");
        }

        _buffer = buffer;
    }

    /// <inheritdoc/>
    public Memory<byte> Buffer => _buffer;

    /// <summary>
    /// Gets the datagram's payload: the bytes after the 8-byte header.
    /// </summary>
    public Memory<byte> Payload => _buffer[8..];

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
    /// Gets or sets the checksum field as it is in the header. Zero means the sender computed none, which only
    /// IPv4 allows.
    /// </summary>
    public ushort Checksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(6, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(6, 2), value);
    }

    /// <summary>
    /// Checks the checksum field against the datagram and the IP pseudo-header.
    /// </summary>
    /// <param name="sourceAddress">The source address of the IP packet, 4 or 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, of the same length.</param>
    /// <returns>
    /// <see langword="true"/> if the checksum is valid, or is zero over IPv4, where zero means none was
    /// computed (RFC 768); otherwise, <see langword="false"/>. Over IPv6 a zero checksum is invalid (RFC 8200,
    /// section 8.1).
    /// </returns>
    /// <exception cref="ArgumentException">The checksum is not zero and the addresses are not both 4 or both 16 bytes long.</exception>
    public bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        if (Checksum == 0)
            return sourceAddress.Length == 4 && destinationAddress.Length == 4;

        return PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)IpProtocol.Udp, _buffer.Span) == 0;
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
    /// Computes the checksum the datagram calls for, without writing it.
    /// </summary>
    /// <remarks>
    /// A computed zero is returned as 0xFFFF, since a zero field means no checksum (RFC 768). The checksum field
    /// is zero while the sum runs and is restored after, so no other thread may use the packet meanwhile.
    /// </remarks>
    /// <param name="sourceAddress">The source address of the IP packet, 4 or 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, of the same length.</param>
    /// <returns>The checksum, never zero, as a number to write in network byte order.</returns>
    /// <exception cref="ArgumentException">The addresses are not both 4 or both 16 bytes long.</exception>
    public ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        var orgChecksum = Checksum;
        Checksum = 0;

        // a computed zero goes as all ones, as a zero field says there is no checksum
        try {
            var checksum = PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)IpProtocol.Udp, _buffer.Span);
            return checksum == 0 ? (ushort)0xFFFF : checksum;
        }
        finally {
            Checksum = orgChecksum;
        }
    }

    /// <summary>
    /// Returns a one-line description of the datagram for logs: its ports and payload length.
    /// </summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        return $"UDP Packet: SrcPort={SourcePort}, DstPort={DestinationPort}, PayloadLen={Payload.Length}";
    }
}

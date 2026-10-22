using System.Buffers.Binary;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

public class UdpPacket : IChecksumPayloadPacket
{
    private readonly Memory<byte> _buffer;

    public UdpPacket(Memory<byte> buffer, bool building)
    {
        if (buffer.Length < 8)
            throw new ArgumentException("Buffer too small for UDP header.", nameof(buffer));

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

    public Memory<byte> Buffer => _buffer;
    public Memory<byte> Payload => _buffer[8..];

    public ushort SourcePort {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span[..2]);
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span[..2], value);
    }

    public ushort DestinationPort {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(2, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(2, 2), value);
    }

    public ushort Checksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(6, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(6, 2), value);
    }

    // With its checksum, a valid datagram sums to all ones, whose complement is zero. A zero field means the sender
    // computed none (RFC 768), which only IPv4 allows (RFC 8200 8.1).
    public bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        if (Checksum == 0)
            return sourceAddress.Length == 4;

        return PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)IpProtocol.Udp, _buffer.Span) == 0;
    }


    public void UpdateChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        Checksum = ComputeChecksum(sourceAddress, destinationAddress);
    }

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

    public override string ToString()
    {
        return $"UDP Packet: SrcPort={SourcePort}, DstPort={DestinationPort}, PayloadLen={Payload.Length}";
    }
}
using System.Buffers.Binary;

namespace VpnHood.Core.Packets;

public class UdpPacket : IChecksumPayloadPacket
{
    private readonly Memory<byte> _buffer;

    public UdpPacket(Memory<byte> buffer, bool building) 
    {
        if (buffer.Length < 8)
            throw new ArgumentException("Buffer too small for UDP header.", nameof(buffer));

        // write udpLength
        if (building) {
            buffer.Span.Clear();
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

    public bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        return ComputeChecksum(sourceAddress, destinationAddress) == Checksum;
    }


    public void UpdateChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        Checksum = ComputeChecksum(sourceAddress, destinationAddress);
    }

    public ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        var orgChecksum = Checksum;
        Checksum = 0;

        try {
            return PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)IpProtocol.Udp, _buffer.Span);
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

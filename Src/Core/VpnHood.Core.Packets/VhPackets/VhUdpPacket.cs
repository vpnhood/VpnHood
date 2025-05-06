using System.Buffers.Binary;

namespace VpnHood.Core.Packets.VhPackets;

public class VhUdpPacket
{
    private readonly Memory<byte> _buffer;

    public VhUdpPacket(Memory<byte> buffer)
    {
        if (buffer.Length < 8)
            throw new ArgumentException("Buffer too small for UDP header.", nameof(buffer));

        var udpLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Span.Slice(4, 2));
        if (udpLength != buffer.Length)
            throw new ArgumentException("Buffer length does not match UDP length field.");

        _buffer = buffer;
    }

    public Memory<byte> Payload => _buffer[8..];

    public ushort SourcePort
    {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span[..2]);
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span[..2], value);
    }

    public ushort DestinationPort
    {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(2, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(2, 2), value);
    }

    public ushort Length
    {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(4, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(4, 2), value);
    }

    public ushort Checksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(6, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(6, 2), value);
    }

    public bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        return ComputeChecksum(sourceAddress, destinationAddress, false) == Checksum;
    }

    public ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        return ComputeChecksum(sourceAddress, destinationAddress, false);
    }

    public ushort UpdateChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        return ComputeChecksum(sourceAddress, destinationAddress, true);
    }

    private ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress, bool update)
    {
        var orgChecksum = Checksum;
        Checksum = 0;

        try {
            return PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)VhIpProtocol.Udp, _buffer.Span);
        }
        finally {
            if (!update)
                Checksum = orgChecksum;
        }
    }

    public override string ToString()
    {
        return $"UDP Packet: SrcPort={SourcePort}, DstPort={DestinationPort}, Len={Length}";
    }
}
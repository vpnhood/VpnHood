using System.Buffers.Binary;

namespace VpnHood.Core.Packets.VhPackets;

public class VhTcpPacket : IChecksumPayloadPacket
{
    private readonly Memory<byte> _buffer;

    public VhTcpPacket(Memory<byte> buffer)
    {
        if (buffer.Length < 20)
            throw new ArgumentException("Buffer too small for TCP header.", nameof(buffer));

        _buffer = buffer;
    }

    public VhTcpPacket(Memory<byte> buffer, int optionsLength)
    {
        if (buffer.Length < 20)
            throw new ArgumentException("Buffer too small for TCP header.", nameof(buffer));

        if (optionsLength is < 0 or > 40)
            throw new ArgumentOutOfRangeException(nameof(optionsLength), "Options length must be between 0 and 40 bytes.");

        if (optionsLength % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(optionsLength), "Options length must be a multiple of 4 bytes.");

        buffer.Span.Clear();

        // Set Data Offset (header length in 32-bit words)
        var dataOffset = (20 + optionsLength) / 4;
        buffer.Span[12] = (byte)(dataOffset << 4);

        _buffer = buffer;
    }

    public Memory<byte> Buffer => _buffer;

    public ushort SourcePort {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span[..2]);
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span[..2], value);
    }

    public ushort DestinationPort {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(2, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(2, 2), value);
    }

    public uint SequenceNumber {
        get => BinaryPrimitives.ReadUInt32BigEndian(_buffer.Span.Slice(4, 4));
        set => BinaryPrimitives.WriteUInt32BigEndian(_buffer.Span.Slice(4, 4), value);
    }

    public uint AcknowledgmentNumber {
        get => BinaryPrimitives.ReadUInt32BigEndian(_buffer.Span.Slice(8, 4));
        set => BinaryPrimitives.WriteUInt32BigEndian(_buffer.Span.Slice(8, 4), value);
    }

    public byte Flags {
        get => _buffer.Span[13];
        set => _buffer.Span[13] = value;
    }

    private void SetFlag(byte mask, bool value)
    {
        Flags = value ? (byte)(Flags | mask) : (byte)(Flags & ~mask);
    }

    public bool Urgent {
        get => (_buffer.Span[13] & 0b0010_0000) != 0;
        set => SetFlag(0b0010_0000, value);
    }

    public bool Acknowledgment {
        get => (_buffer.Span[13] & 0b0001_0000) != 0;
        set => SetFlag(0b0001_0000, value);
    }

    public bool Push {
        get => (_buffer.Span[13] & 0b0000_1000) != 0;
        set => SetFlag(0b0000_1000, value);
    }

    public bool Reset {
        get => (_buffer.Span[13] & 0b0000_0100) != 0;
        set => SetFlag(0b0000_0100, value);
    }

    public bool Synchronize {
        get => (_buffer.Span[13] & 0b0000_0010) != 0;
        set => SetFlag(0b0000_0010, value);
    }

    public bool Finish {
        get => (_buffer.Span[13] & 0b0000_0001) != 0;
        set => SetFlag(0b0000_0001, value);
    }

    public ushort WindowSize {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(14, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(14, 2), value);
    }

    public ushort Checksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(16, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(16, 2), value);
    }

    public ushort UrgentPointer {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(18, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(18, 2), value);
    }

    private byte DataOffset => (byte)((_buffer.Span[12] >> 4) * 4);
    public Memory<byte> Options => _buffer.Slice(20, DataOffset - 20);

    public Memory<byte> Payload => _buffer[DataOffset..];

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
            return PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)VhIpProtocol.Tcp, _buffer.Span);
        }
        finally {
            Checksum = orgChecksum;
        }
    }

    public override string ToString()
    {
        return $"TCP Packet: SrcPort={SourcePort}, DstPort={DestinationPort}, Seq={SequenceNumber}, Ack={AcknowledgmentNumber}, " +
               $"Flags=[Urgent={Urgent}, Ack={Acknowledgment}, Push={Push}, Reset={Reset}, Sync={Synchronize}, Fin={Finish}], " +
               $"PayloadLen={Payload.Length}";
    }
}

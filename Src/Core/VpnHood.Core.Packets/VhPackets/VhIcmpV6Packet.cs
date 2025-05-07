using System;
using System.Buffers.Binary;

namespace VpnHood.Core.Packets.VhPackets;

public class VhIcmpV6Packet : IChecksumPayloadPacket
{
    private readonly Memory<byte> _buffer;
    public Memory<byte> Buffer => _buffer;

    public VhIcmpV6Packet(Memory<byte> buffer, bool building)
    {
        if (buffer.Length < 4)
            throw new ArgumentException("Buffer too small for ICMPv6 header.");

        if (buffer.Length > 0xFFFF)
            throw new ArgumentException("Buffer too large for ICMPv6 packet.");

        if (building)
            buffer.Span.Clear();

        _buffer = buffer;
    }

    public IcmpV6Type Type {
        get => (IcmpV6Type)_buffer.Span[0];
        set => _buffer.Span[0] = (byte)value;
    }

    public byte Code {
        get => _buffer.Span[1];
        set => _buffer.Span[1] = value;
    }

    public ushort Checksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(2, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(2, 2), value);
    }

    public ushort Identifier {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(4, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(4, 2), value);
    }

    public ushort SequenceNumber {
        get => BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(6, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(6, 2), value);
    }

    public Memory<byte> Payload => _buffer[8..];

    public bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress) =>
        ComputeChecksum(sourceAddress, destinationAddress) == Checksum;

    public void UpdateChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress) =>
        Checksum = ComputeChecksum(sourceAddress, destinationAddress);

    public ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress)
    {
        var orgChecksum = Checksum;
        Checksum = 0;

        try {
            return PacketUtil.ComputeChecksum(sourceAddress, destinationAddress, (byte)VhIpProtocol.IcmpV6, _buffer.Span);
        }
        finally {
            Checksum = orgChecksum;
        }
    }

    public override string ToString()
    {
        return $"ICMPv6 Packet: Type={Type}, Code={Code}, Id={Identifier}, Seq={SequenceNumber}, " +
               $"PayloadLen={Payload.Length}";
    }
}
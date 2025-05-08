using System.Buffers.Binary;

namespace VpnHood.Core.Packets.VhPackets;

public class VhIcmpV6Packet : IChecksumPayloadPacket
{
    private readonly Memory<byte> _buffer;
    public Memory<byte> Buffer => _buffer;

    public VhIcmpV6Packet(Memory<byte> buffer, bool building)
    {
        // Base ICMPv6 header requires at least 8 bytes, but Echo messages require 12 bytes minimum
        if (buffer.Length < 8)
            throw new ArgumentException("Buffer too small for ICMPv6 header.");

        if (buffer.Length < 12 && (buffer.Span[0] == (byte)IcmpV6Type.EchoRequest || buffer.Span[0] == (byte)IcmpV6Type.EchoReply))
            throw new ArgumentException("Buffer too small for ICMPv6 Echo message (requires at least 12 bytes).");

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

    public uint MessageSpecific {
        get => BinaryPrimitives.ReadUInt32BigEndian(_buffer.Span.Slice(4, 4));
        set => BinaryPrimitives.WriteUInt32BigEndian(_buffer.Span.Slice(4, 4), value);
    }

    /// <summary>
    /// Identifier used in Echo Request and Echo Reply messages.
    /// - Returns 0 if the Type is not EchoRequest or EchoReply.
    /// - Throws if you attempt to set it on a non-Echo packet.
    /// </summary>
    public ushort Identifier {
        get => IsEcho ? BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(8, 2)) : (ushort)0;
        set {
            if (!IsEcho)
                throw new InvalidOperationException("Identifier is only valid for Echo messages.");
            BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(8, 2), value);
        }
    }

    /// <summary>
    /// Sequence number used in Echo Request and Echo Reply messages.
    /// - Returns 0 if the Type is not EchoRequest or EchoReply.
    /// - Throws if you attempt to set it on a non-Echo packet.
    /// </summary>
    public ushort SequenceNumber {
        get => IsEcho ? BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(10, 2)) : (ushort)0;
        set {
            if (!IsEcho)
                throw new InvalidOperationException("SequenceNumber is only valid for Echo messages.");
            BinaryPrimitives.WriteUInt16BigEndian(_buffer.Span.Slice(10, 2), value);
        }
    }

    public bool IsEcho => Type == IcmpV6Type.EchoRequest || Type == IcmpV6Type.EchoReply;

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
        var str = $"ICMPv6 Packet: Type={Type}, Code={Code}, PayloadLen={Payload.Length}";
        if (!IsEcho)
            str += $", MsgSpec={MessageSpecific}";
        else
            str += $", Id={Identifier}, Seq={SequenceNumber}";
        return str;
    }
}

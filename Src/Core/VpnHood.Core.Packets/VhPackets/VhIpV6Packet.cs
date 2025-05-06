using System.Buffers.Binary;

namespace VpnHood.Core.Packets.VhPackets;

public class VhIpV6Packet : VhIpPacket
{
    public VhIpV6Packet(Memory<byte> buffer) : base(buffer)
    {
        // Check if the buffer is large enough for the IP header
        if (buffer.Length < 40)
            throw new ArgumentException("Buffer too small for IPv6 header.");

        // Check if the version is 6
        var version = buffer.Span[0] >> 4;
        if (version != 6)
            throw new ArgumentException("Invalid IP version. Expected IPv6.");

        // Check if the payment length is valid
        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Span.Slice(4, 2));
        if (buffer.Length != 40 + payloadLength)
            throw new ArgumentException("Buffer length does not match IPv6 payload length field.");
    }

    private Span<byte> Span => Buffer.Span;
    public override VhIpProtocol Protocol => NextHeader;
    public VhIpProtocol NextHeader => (VhIpProtocol)Span[6];
    public override Memory<byte> Header => Buffer[..40];
    public override byte TimeToLive {
        get => HopLimit;
        set => HopLimit = (byte)value;
    }

    public byte HopLimit {
        get => Span[7];
        set => Span[7] = value;
    }
    
    
    
    public override Span<byte> SourceAddressSpan {
        get => Span.Slice(8, 16);
        set {
            value.CopyTo(Span.Slice(8, 16));
            SourceAddressField = null;
        }
    }

    public override Span<byte> DestinationAddressSpan {
        get => Span.Slice(24, 16);
        set {
            value.CopyTo(Span.Slice(24, 16));
            DestinationAddressField = null;
        }
    }

    public int FlowLabel {
        get {
            // Extract 20-bit Flow Label: lower 4 bits of Span[1], all Span[2] and Span[3]
            return ((Span[1] & 0x0F) << 16) | (Span[2] << 8) | Span[3];
        }
        set {
            if (value is < 0 or > 0xFFFFF)
                throw new ArgumentOutOfRangeException(nameof(value), "Flow Label must be a 20-bit unsigned value (0 to 1048575).");

            // Write back 20-bit Flow Label
            Span[1] = (byte)((Span[1] & 0xF0) | ((value >> 16) & 0x0F));
            Span[2] = (byte)((value >> 8) & 0xFF);
            Span[3] = (byte)(value & 0xFF);
        }
    }


    public byte TrafficClass {
        get {
            // TrafficClass = low 4 bits of Span[0] and high 4 bits of Span[1]
            return (byte)(((Span[0] & 0x0F) << 4) | (Span[1] >> 4));
        }
        set {
            // value is 8 bits: upper 4 bits go to lower nibble of Span[0], lower 4 bits go to upper nibble of Span[1]
            Span[0] = (byte)((Span[0] & 0xF0) | (value >> 4));     // Preserve version (upper 4 bits)
            Span[1] = (byte)((Span[1] & 0x0F) | (value << 4));     // Preserve FlowLabel high bits
        }
    }
}
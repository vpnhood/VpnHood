using System.Net;

namespace VpnHood.Core.Packets.VhPackets;

public abstract class VhIpPacket(Memory<byte> buffer)
{
    protected IPAddress? SourceAddressField;
    protected IPAddress? DestinationAddressField;
    internal object? PayloadPacket { get; set; }

    public Memory<byte> Buffer { get; init; } = buffer;

    public VhIpVersion Version {
        get { return (VhIpVersion)(Buffer.Span[0] >> 4); }
        protected set { Buffer.Span[0] = (byte)((byte)value << 4); }
    }

    public abstract Span<byte> SourceAddressSpan { get; set; }
    public abstract Span<byte> DestinationAddressSpan { get; set; }

    public IPAddress SourceAddress {
        get => SourceAddressField ??= new IPAddress(SourceAddressSpan);
        set {
            if (!value.TryWriteBytes(SourceAddressSpan, out var written) || written != SourceAddressSpan.Length)
                throw new ArgumentException("Invalid IP address format.");
            SourceAddressField = value;
        }
    }

    public IPAddress DestinationAddress {
        get => DestinationAddressField ??= new IPAddress(DestinationAddressSpan);
        set {
            if (!value.TryWriteBytes(DestinationAddressSpan, out var written) || written != DestinationAddressSpan.Length)
                throw new ArgumentException("Invalid IP address format.");
            DestinationAddressField = value;
        }
    }
    public abstract VhIpProtocol Protocol { get; }
    public abstract byte TimeToLive { get; set; }
    public abstract Memory<byte> Header { get; }
    public Memory<byte> Payload => Buffer[Header.Length..];

    public override string ToString()
    {
        return $"Packet: Src={SourceAddress}, Dst={DestinationAddress}, Proto={Protocol}, " +
               $"TotalLength:{Buffer.Length}, PayloadLen={Payload.Length}";
    }
}
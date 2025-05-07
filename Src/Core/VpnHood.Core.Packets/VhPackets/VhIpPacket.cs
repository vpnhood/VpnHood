using System.Net;

namespace VpnHood.Core.Packets.VhPackets;

public abstract class VhIpPacket(Memory<byte> buffer) : IDisposable
{
    private bool _disposed;
    protected IPAddress? SourceAddressField;
    protected IPAddress? DestinationAddressField;
    private IPayloadPacket? _payloadPacket;
    public Memory<byte> Buffer => _disposed ? throw new ObjectDisposedException(nameof(VhIpPacket)) : buffer;

    public IPayloadPacket? PayloadPacket {
        get => _payloadPacket;
        set => _payloadPacket = value == null || value.Buffer.Equals(Payload)
            ? value
            : throw new InvalidOperationException("The PayloadPacket buffer must match the packet buffer.");
    }

    public VhIpVersion Version {
        get { return (VhIpVersion)(Buffer.Span[0] >> 4); }
        protected set => Buffer.Span[0] = (byte)((byte)value << 4 | (Buffer.Span[0] & 0x0F));
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

    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;
    }

    ~VhIpPacket() => Dispose(false);
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

}
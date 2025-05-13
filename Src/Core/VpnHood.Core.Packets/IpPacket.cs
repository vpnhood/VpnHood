using System.Net;
using System.Text;

namespace VpnHood.Core.Packets;

public abstract class IpPacket(Memory<byte> buffer) : IDisposable
{
    private bool _disposed;
    protected IPAddress? SourceAddressField;
    protected IPAddress? DestinationAddressField;
    private IPayloadPacket? _payloadPacket;
    public Memory<byte> Buffer => _disposed ? throw new ObjectDisposedException(nameof(IpPacket)) : buffer;
    public int PacketLength => buffer.Length;

    public IPayloadPacket? PayloadPacket {
        get => _payloadPacket;
        set => _payloadPacket = value == null || value.Buffer.Equals(Payload)
            ? value
            : throw new InvalidOperationException("The PayloadPacket buffer must match the packet buffer.");
    }

    public IpVersion Version {
        get { return (IpVersion)(Buffer.Span[0] >> 4); }
        protected set => Buffer.Span[0] = (byte)((byte)value << 4 | Buffer.Span[0] & 0x0F);
    }

    protected abstract Span<byte> SourceAddressBuffer { get; set; }
    protected abstract Span<byte> DestinationAddressBuffer { get; set; }


    public ReadOnlySpan<byte> SourceAddressSpan {
        get => SourceAddressBuffer;
        set {
            if (!value.TryCopyTo(SourceAddressBuffer))
                throw new ArgumentException("Invalid IP address format.");
            SourceAddressBuffer = null;
        }
    }

    public ReadOnlySpan<byte> DestinationAddressSpan {
        get => DestinationAddressBuffer;
        set {
            if (!value.TryCopyTo(DestinationAddressBuffer))
                throw new ArgumentException("Invalid IP address format.");
            DestinationAddressBuffer = null;
        }
    }

    public IPAddress SourceAddress {
        get => SourceAddressField ??= new IPAddress(SourceAddressSpan);
        set {
            if (!value.TryWriteBytes(SourceAddressBuffer, out var written) || written != SourceAddressSpan.Length)
                throw new ArgumentException("Invalid IP address format.");
            SourceAddressField = value;
        }
    }

    public IPAddress DestinationAddress {
        get => DestinationAddressField ??= new IPAddress(DestinationAddressSpan);
        set {
            if (!value.TryWriteBytes(DestinationAddressBuffer, out var written) || written != DestinationAddressSpan.Length)
                throw new ArgumentException("Invalid IP address format.");
            DestinationAddressField = value;
        }
    }
    public abstract IpProtocol Protocol { get; }
    public abstract byte TimeToLive { get; set; }
    public abstract Memory<byte> Header { get; }
    public Memory<byte> Payload => Buffer[Header.Length..];

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append($"Packet: Src={SourceAddress}, Dst={DestinationAddress}, Proto={Protocol}, ");
        builder.Append($"TotalLength:{Buffer.Length}, PayloadLen={Payload.Length}");

        if (PayloadPacket != null)
            builder.Append($", PayloadPacket: {PayloadPacket.GetType().Name}, {PayloadPacket}");

        return builder.ToString();
    }

    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;
    }


    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~IpPacket() => Dispose(false);
}
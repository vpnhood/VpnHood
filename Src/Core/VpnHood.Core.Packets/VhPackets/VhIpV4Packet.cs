using System.Buffers;
using System.Buffers.Binary;

namespace VpnHood.Core.Packets.VhPackets;

public class VhIpV4Packet : VhIpPacket
{
    private bool _disposed;
    private readonly IMemoryOwner<byte>? _memoryOwner;

    public VhIpV4Packet(IMemoryOwner<byte> memoryOwner, int packetLength)
        : this(memoryOwner.Memory[..packetLength])
    {
        _memoryOwner = memoryOwner;
    }

    public VhIpV4Packet(IMemoryOwner<byte> memoryOwner, int packetLength, VhIpProtocol protocol, int optionsLength) 
        : this(memoryOwner.Memory[..packetLength], protocol, optionsLength)
    {
        _memoryOwner = memoryOwner;
    }

    public VhIpV4Packet(Memory<byte> buffer, VhIpProtocol protocol, int optionsLength) 
        : base(buffer)
    {
        // validate buffer length
        if (buffer.Length is < 20 or > 0xFFFF)
            throw new ArgumentException("Buffer too small for IP header.", nameof(buffer));

        // clean buffer
        buffer.Span.Clear();

        // validate options length
        if (optionsLength is < 0 or > 40)
            throw new ArgumentOutOfRangeException(nameof(optionsLength), "Options length must be between 0 and 40 bytes.");

        if (optionsLength % 4 !=0)
            throw new ArgumentOutOfRangeException(nameof(optionsLength), "Options length must be a multiple of 4 bytes.");

        // set version
        Version = VhIpVersion.IPv4;

        // set protocol
        Span[9] = (byte)protocol;

        // set header length
        var hl = (20 + optionsLength) / 4;
        Span[0] = (byte)((4 << 4) | hl);

        // set total length
        BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(2, 2), (ushort)buffer.Length);
    }


    public VhIpV4Packet(Memory<byte> buffer) 
        : base(buffer)
    {
        // Check if the buffer is large enough for the IP header
        if (buffer.Length is < 20 or > 0xFFFF)
            throw new ArgumentException("Buffer too small for IP header.", nameof(buffer));

        // Check if the version is 6
        var version = buffer.Span[0] >> 4;
        if (version != 4)
            throw new ArgumentException("Invalid IP version. Expected IPv6.");

        // Check if the header length is valid
        var ihl = Span[0] & 0x0F;
        if (ihl < 5)
            throw new ArgumentException("Invalid Internet Header Length.");

        // Check if the header length is within the buffer size
        var headerLength = ihl * 4;
        if (buffer.Length < headerLength)
            throw new ArgumentException("Buffer too small for IP header.");

        // check total length
        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Span.Slice(2, 2));
        if (buffer.Length != totalLength)
            throw new ArgumentException("Buffer length does not match IP total length field.");
    }

    private Span<byte> Span => Buffer.Span;
    public override Memory<byte> Header => Buffer[..(InternetHeaderLength * 4)];
    public override VhIpProtocol Protocol => (VhIpProtocol)Span[9];
    public int InternetHeaderLength => Span[0] & 0x0F;


    public byte TypeOfService {
        get => Span[1];
        set => Span[1] = value;
    }
    public ushort Identification {
        get => BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(4, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(4, 2), value);
    }
    public byte FragmentFlags {
        get => (byte)(Span[6] >> 5);
        set => Span[6] = (byte)(value << 5 | Span[6] & 0x1F);
    }
    public bool DontFragment {
        get => (Span[6] & 0x40) != 0;
        set {
            if (value) Span[6] |= 0x40;
            else Span[6] &= 0xBF;
        }
    }
    public bool MoreFragments {
        get => (Span[6] & 0x20) != 0;
        set {
            if (value) Span[6] |= 0x20;
            else Span[6] &= 0xDF;
        }
    }
    public int FragmentOffset {
        get => (ushort)(BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(6, 2)) & 0x1FFF);
        set {
            var flags = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(6, 2)) & 0xE000);
            var combined = (ushort)(flags | value & 0x1FFF);
            BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(6, 2), combined);
        }
    }

    // ReSharper disable once IdentifierTypo
    public byte Dscp {
        get => (byte)((Span[1] & 0xFC) >> 2);
        set => Span[1] = (byte)((Span[1] & 0x03) | ((value & 0x3F) << 2));
    }

    public IpEcnField Ecn {
        get => (IpEcnField)(Span[1] & 0x03);
        set => Span[1] = (byte)((Span[1] & 0xFC) | ((byte)value & 0x03));
    }

    public override byte TimeToLive {
        get => Span[8];
        set => Span[8] = value;
    }

    public ushort HeaderChecksum
    {
        get => BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(10, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(10, 2), value);
    }

    public override Span<byte> SourceAddressSpan {
        get => Span.Slice(12, 4);
        set {
            value.CopyTo(Span.Slice(12, 4));
            SourceAddressField = null;
        }
    }

    public override Span<byte> DestinationAddressSpan {
        get => Span.Slice(16, 4);
        set {
            value.CopyTo(Span.Slice(16, 4));
            DestinationAddressField = null;
        }
    }
    public Memory<byte> Options => Buffer.Slice(20, (InternetHeaderLength * 4) - 20);
    public bool IsHeaderChecksumValid()
    {
        var originalChecksum = HeaderChecksum;
        Span[10] = 0;
        Span[11] = 0;
        var calculated = ComputeChecksum(Span[..Header.Length]);
        Span[10] = (byte)(originalChecksum >> 8);
        Span[11] = (byte)(originalChecksum & 0xFF);
        return originalChecksum == calculated;
    }

    public void UpdateHeaderChecksum(VhIpPacket ipPacket)
    {
        Span[10] = 0;
        Span[11] = 0;
        HeaderChecksum = ComputeChecksum(Span[..Header.Length]);
    }

    private static ushort ComputeChecksum(ReadOnlySpan<byte> data)
    {
        // checksum calculation is done on 16-bit words
        uint sum = 0;
        for (var i = 0; i < data.Length; i += 2) {
            var word = (ushort)((data[i] << 8) + (i + 1 < data.Length ? data[i + 1] : 0));
            sum += word;
        }

        while (sum >> 16 != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);

        return (ushort)~sum;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        
        // I intentionally dispose the memory owner as unmanaged
        // because the memory owner does have finalizer
        _memoryOwner?.Dispose();
        base.Dispose(disposing);
        _disposed = true;
    }

    ~VhIpV4Packet() => Dispose(false);
}
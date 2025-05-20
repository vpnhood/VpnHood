using System.Buffers;
using System.Buffers.Binary;

namespace VpnHood.Core.Packets;

public class IpV4Packet : IpPacket
{
    private bool _disposed;
    private readonly IMemoryOwner<byte>? _memoryOwner;

    public IpV4Packet(IMemoryOwner<byte> memoryOwner)
        : this(memoryOwner.Memory)
    {
        _memoryOwner = memoryOwner;
    }

    public IpV4Packet(IMemoryOwner<byte> memoryOwner, int packetLength, IpProtocol protocol, int optionsLength)
        : this(memoryOwner.Memory[..packetLength], protocol, optionsLength)
    {
        _memoryOwner = memoryOwner;
    }

    public IpV4Packet(Memory<byte> buffer, IpProtocol protocol, int optionsLength)
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

        if (optionsLength % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(optionsLength), "Options length must be a multiple of 4 bytes.");

        // set version
        Version = IpVersion.IPv4;

        // set protocol
        Span[9] = (byte)protocol;

        // set header length
        var hl = (20 + optionsLength) / 4;
        Span[0] = (byte)(4 << 4 | hl);

        // set total length
        BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(2, 2), (ushort)buffer.Length);
    }

    private static Memory<byte> AdjustBuffer(Memory<byte> buffer)
    {
        var packetLength = GetPacketLength(buffer.Span);
        if (packetLength < 20)
            throw new ArgumentException("Invalid IPv4 packet length.", nameof(buffer));

        if (packetLength > buffer.Length)
            throw new ArgumentException("Buffer too small for IPv4 packet.", nameof(buffer));

        return buffer[..packetLength];
    }

    public IpV4Packet(Memory<byte> buffer)
        : base(AdjustBuffer(buffer))
    {
        buffer = Buffer;

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
    public override IpProtocol Protocol => (IpProtocol)Span[9];
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
        set => Span[1] = (byte)(Span[1] & 0x03 | (value & 0x3F) << 2);
    }

    public IpEcnField Ecn {
        get => (IpEcnField)(Span[1] & 0x03);
        set => Span[1] = (byte)(Span[1] & 0xFC | (byte)value & 0x03);
    }

    public override byte TimeToLive {
        get => Span[8];
        set => Span[8] = value;
    }

    public ushort HeaderChecksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(10, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(10, 2), value);
    }

    protected override Span<byte> SourceAddressBuffer {
        get => Span.Slice(12, 4);
        set {
            value.CopyTo(Span.Slice(12, 4));
            SourceAddressField = null;
        }
    }

    protected override Span<byte> DestinationAddressBuffer {
        get => Span.Slice(16, 4);
        set {
            value.CopyTo(Span.Slice(16, 4));
            DestinationAddressField = null;
        }
    }
    public Memory<byte> Options => Buffer.Slice(20, InternetHeaderLength * 4 - 20);
    public bool IsHeaderChecksumValid()
    {
        var originalChecksum = HeaderChecksum;
        Span[10] = 0;
        Span[11] = 0;
        var calculated = PacketUtil.OnesComplementSum(Span[..Header.Length]);
        Span[10] = (byte)(originalChecksum >> 8);
        Span[11] = (byte)(originalChecksum & 0xFF);
        return originalChecksum == calculated;
    }

    public void UpdateHeaderChecksum(IpPacket ipPacket)
    {
        Span[10] = 0;
        Span[11] = 0;
        HeaderChecksum = PacketUtil.OnesComplementSum(Span[..Header.Length]);
    }

    public static int GetPacketLength(ReadOnlySpan<byte> buffer)
    {
        if (GetPacketVersion(buffer) != IpVersion.IPv4)
            throw new ArgumentException("Buffer is not an IPv4 packet.", nameof(buffer));

        if (buffer.Length < 4)
            throw new ArgumentException("IPv4 header requires at least 4 bytes to determine packet length.", nameof(buffer));

        return (ushort)(buffer[2] << 8 | buffer[3]);
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

    ~IpV4Packet() => Dispose(false);
}
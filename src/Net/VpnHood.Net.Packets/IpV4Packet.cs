using System.Buffers;
using System.Buffers.Binary;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

/// <summary>
/// Represents an IPv4 packet (RFC 791) over a buffer, which it reads and writes in place.
/// </summary>
/// <remarks>
/// A constructor that takes a <see cref="Memory{T}"/> leaves the memory to the caller; one that takes an
/// <see cref="IMemoryOwner{T}"/> owns it and disposes it with the packet. Changing a header field does not
/// update the header checksum: call <see cref="UpdateHeaderChecksum"/> or
/// <see cref="Extensions.IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
/// </remarks>
public class IpV4Packet : IpPacket
{
    private bool _disposed;
    private readonly IMemoryOwner<byte>? _memoryOwner;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpV4Packet"/> class by parsing the packet at the start of
    /// rented memory, which the packet then owns.
    /// </summary>
    /// <param name="memoryOwner">
    /// The memory, which holds the packet from its first byte and may be longer. The packet disposes it; when
    /// parsing fails, it stays the caller's.
    /// </param>
    /// <exception cref="ArgumentException">The bytes are no valid IPv4 packet, or the memory is shorter than the packet.</exception>
    /// <exception cref="IndexOutOfRangeException">The memory is empty.</exception>
    public IpV4Packet(IMemoryOwner<byte> memoryOwner)
        : this(memoryOwner.Memory)
    {
        _memoryOwner = memoryOwner;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpV4Packet"/> class by building a new packet in rented
    /// memory, which the packet then owns.
    /// </summary>
    /// <param name="memoryOwner">The memory, at least <paramref name="packetLength"/> bytes. The packet disposes it.</param>
    /// <param name="packetLength">The packet's total length in bytes, from 20 to 65535.</param>
    /// <param name="protocol">The protocol of the payload.</param>
    /// <param name="optionsLength">The length of the header options, a multiple of 4 from 0 to 40.</param>
    /// <exception cref="ArgumentException"><paramref name="packetLength"/> is below 20 or above 65535.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="optionsLength"/> is out of range or not a multiple of 4, or <paramref name="packetLength"/>
    /// exceeds the memory.
    /// </exception>
    public IpV4Packet(IMemoryOwner<byte> memoryOwner, int packetLength, IpProtocol protocol, int optionsLength)
        : this(memoryOwner.Memory[..packetLength], protocol, optionsLength)
    {
        _memoryOwner = memoryOwner;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpV4Packet"/> class by building a new packet in a buffer.
    /// </summary>
    /// <remarks>
    /// The whole buffer is cleared, then the version, the header length, the protocol and the total length are
    /// written. The addresses, the time to live and the checksum are left zero.
    /// </remarks>
    /// <param name="buffer">The buffer, exactly as long as the packet: from 20 to 65535 bytes.</param>
    /// <param name="protocol">The protocol of the payload.</param>
    /// <param name="optionsLength">The length of the header options, a multiple of 4 from 0 to 40.</param>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> is shorter than 20 or longer than 65535 bytes.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="optionsLength"/> is out of range or not a multiple of 4.</exception>
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
            throw new ArgumentOutOfRangeException(nameof(optionsLength),
                "Options length must be between 0 and 40 bytes.");

        if (optionsLength % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(optionsLength),
                "Options length must be a multiple of 4 bytes.");

        // set version
        Version = IpVersion.IPv4;

        // set protocol
        Span[9] = (byte)protocol;

        // set header length
        var hl = (20 + optionsLength) / 4;
        Span[0] = (byte)((4 << 4) | hl);

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

    /// <summary>
    /// Initializes a new instance of the <see cref="IpV4Packet"/> class by parsing the packet at the start of a
    /// buffer.
    /// </summary>
    /// <remarks>
    /// The packet takes the buffer's first total-length bytes and leaves the rest. It checks the header's
    /// version and lengths, not its checksum.
    /// </remarks>
    /// <param name="buffer">The buffer, which holds the packet from its first byte and may be longer.</param>
    /// <exception cref="ArgumentException">
    /// The bytes are no IPv4 packet, their header length or total length is invalid, or the buffer is shorter
    /// than the packet.
    /// </exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="buffer"/> is empty.</exception>
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

    /// <inheritdoc/>
    public override Memory<byte> Header => Buffer[..(InternetHeaderLength * 4)];

    /// <inheritdoc/>
    public override IpProtocol Protocol => (IpProtocol)Span[9];

    /// <summary>
    /// Gets a value indicating whether the packet is a fragment: More Fragments is set, or the fragment offset
    /// is not zero.
    /// </summary>
    public override bool IsFragment => ((Span[6] & 0x3F) | Span[7]) != 0; // more fragments, or an offset

    /// <summary>
    /// Gets the header length in 32-bit words, from 5 to 15.
    /// </summary>
    public int InternetHeaderLength => Span[0] & 0x0F;

    /// <summary>
    /// Gets or sets the whole type-of-service byte, which holds <see cref="Dscp"/> and <see cref="Ecn"/>.
    /// </summary>
    public byte TypeOfService {
        get => Span[1];
        set => Span[1] = value;
    }

    /// <summary>
    /// Gets or sets the identification, which the fragments of one datagram share.
    /// </summary>
    public ushort Identification {
        get => BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(4, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(4, 2), value);
    }

    /// <summary>
    /// Gets or sets the 3 flag bits: reserved, Don't Fragment and More Fragments, from the highest.
    /// </summary>
    public byte FragmentFlags {
        get => (byte)(Span[6] >> 5);
        set => Span[6] = (byte)((value << 5) | (Span[6] & 0x1F));
    }

    /// <summary>
    /// Gets or sets a value indicating whether routers may not fragment the packet.
    /// </summary>
    // ReSharper disable once IdentifierTypo
    public bool DontFragment {
        get => (Span[6] & 0x40) != 0;
        set {
            if (value) Span[6] |= 0x40;
            else Span[6] &= 0xBF;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether more fragments of the datagram follow this one.
    /// </summary>
    public bool MoreFragments {
        get => (Span[6] & 0x20) != 0;
        set {
            if (value) Span[6] |= 0x20;
            else Span[6] &= 0xDF;
        }
    }

    /// <summary>
    /// Gets or sets the fragment's place in the datagram, in units of 8 bytes, from 0 to 8191.
    /// </summary>
    /// <remarks>
    /// A value is masked to its low 13 bits, so a larger one wraps.
    /// </remarks>
    public int FragmentOffset {
        get => (ushort)(BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(6, 2)) & 0x1FFF);
        set {
            var flags = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(6, 2)) & 0xE000);
            var combined = (ushort)(flags | (value & 0x1FFF));
            BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(6, 2), combined);
        }
    }

    /// <summary>
    /// Gets or sets the Differentiated Services Code Point, the high 6 bits of the type-of-service byte.
    /// </summary>
    /// <remarks>
    /// A value is masked to its low 6 bits.
    /// </remarks>
    // ReSharper disable once IdentifierTypo
    public byte Dscp {
        get => (byte)((Span[1] & 0xFC) >> 2);
        set => Span[1] = (byte)((Span[1] & 0x03) | ((value & 0x3F) << 2));
    }

    /// <summary>
    /// Gets or sets the Explicit Congestion Notification codepoint, the low 2 bits of the type-of-service byte.
    /// </summary>
    public IpEcnField Ecn {
        get => (IpEcnField)(Span[1] & 0x03);
        set => Span[1] = (byte)((Span[1] & 0xFC) | ((byte)value & 0x03));
    }

    /// <summary>
    /// Gets or sets the time to live: the number of hops the packet may still take.
    /// </summary>
    public override byte TimeToLive {
        get => Span[8];
        set => Span[8] = value;
    }

    /// <summary>
    /// Gets or sets the header checksum field as it is in the header.
    /// </summary>
    public ushort HeaderChecksum {
        get => BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(10, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(10, 2), value);
    }

    private protected override Span<byte> SourceAddressBuffer => Span.Slice(12, 4);

    private protected override Span<byte> DestinationAddressBuffer => Span.Slice(16, 4);

    /// <summary>
    /// Gets the header options: the bytes between the 20-byte fixed header and the payload.
    /// </summary>
    public Memory<byte> Options => Buffer.Slice(20, InternetHeaderLength * 4 - 20);

    /// <summary>
    /// Checks the header checksum against the header.
    /// </summary>
    /// <returns><see langword="true"/> if the checksum is valid; otherwise, <see langword="false"/>.</returns>
    // with its checksum, a valid header sums to all ones, whose complement is zero, whichever zero the field holds
    public bool IsHeaderChecksumValid()
    {
        return PacketUtil.OnesComplementSum(Span[..Header.Length]) == 0;
    }

    /// <summary>
    /// Computes the header checksum and writes it to <see cref="HeaderChecksum"/>.
    /// </summary>
    public void UpdateHeaderChecksum()
    {
        Span[10] = 0;
        Span[11] = 0;
        HeaderChecksum = PacketUtil.OnesComplementSum(Span[..Header.Length]);
    }

    /// <summary>
    /// Reads an IPv4 packet's total length from its header, without parsing the rest.
    /// </summary>
    /// <param name="buffer">The packet's first bytes, at least 4.</param>
    /// <returns>The total length field: the packet's length in bytes, header included.</returns>
    /// <exception cref="ArgumentException">The bytes are no IPv4 packet, or fewer than 4.</exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="buffer"/> is empty.</exception>
    public static int GetPacketLength(ReadOnlySpan<byte> buffer)
    {
        if (GetPacketVersion(buffer) != IpVersion.IPv4)
            throw new ArgumentException("Buffer is not an IPv4 packet.", nameof(buffer));

        if (buffer.Length < 4)
            throw new ArgumentException("IPv4 header requires at least 4 bytes to determine packet length.",
                nameof(buffer));

        return (ushort)((buffer[2] << 8) | buffer[3]);
    }

    /// <summary>
    /// Disposes the memory the packet owns, if any, and marks the packet disposed.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release
    /// only unmanaged resources.
    /// </param>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
            _memoryOwner?.Dispose();

        base.Dispose(disposing);
        _disposed = true;
    }
}

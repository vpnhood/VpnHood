using System.Buffers.Binary;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

/// <summary>
/// Represents an IPv4 packet (RFC 791) over a buffer, which it reads and writes in place.
/// </summary>
/// <remarks>
/// The <see cref="PacketMemory"/> a packet is made on says whether the packet owns its memory. Changing a header
/// field does not update the header checksum: call <see cref="UpdateHeaderChecksum"/> or
/// <see cref="Extensions.IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
/// </remarks>
public class IpV4Packet : IpPacket
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpV4Packet"/> class by building a new packet at the start of some
    /// memory.
    /// </summary>
    /// <remarks>
    /// The packet's bytes are cleared, then the version, the header length, the protocol and the total length are
    /// written. The addresses, the time to live and the checksum are left zero. A build that is refused writes
    /// nothing.
    /// </remarks>
    /// <param name="memory">
    /// The memory, at least <paramref name="packetLength"/> bytes: a <see cref="Memory{T}"/> or an array the caller
    /// keeps, or memory given to the packet (see <see cref="PacketMemory"/>), which stays the caller's when building
    /// fails.
    /// </param>
    /// <param name="packetLength">
    /// The packet's total length in bytes, from 20 to 65535, and at least 20 plus <paramref name="optionsLength"/>.
    /// </param>
    /// <param name="protocol">The protocol of the payload.</param>
    /// <param name="optionsLength">The length of the header options, a multiple of 4 from 0 to 40.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="packetLength"/> is below 20, above 65535, or shorter than the header with its options.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="optionsLength"/> is out of range or not a multiple of 4, or <paramref name="packetLength"/> is
    /// negative or exceeds the memory.
    /// </exception>
    public IpV4Packet(PacketMemory memory, int packetLength, IpProtocol protocol, int optionsLength)
        : base(memory.Slice(packetLength))
    {
        var buffer = Buffer;

        // every check comes before the first write: a refused build leaves the caller's memory as it was
        if (buffer.Length < 20)
            throw new ArgumentException("Buffer too small for IP header.", nameof(packetLength));

        // the total length field holds at most 65535: a longer packet would say it is shorter
        if (buffer.Length > 0xFFFF)
            throw new ArgumentException("Packet too long for the IPv4 total length field.", nameof(packetLength));

        if (optionsLength is < 0 or > 40)
            throw new ArgumentOutOfRangeException(nameof(optionsLength),
                "Options length must be between 0 and 40 bytes.");

        if (optionsLength % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(optionsLength),
                "Options length must be a multiple of 4 bytes.");

        // the header length field counts the options: past the packet's end, Header and Payload would throw
        if (buffer.Length < 20 + optionsLength)
            throw new ArgumentException("Buffer too small for IP header and its options.", nameof(packetLength));

        // clean buffer
        buffer.Span.Clear();

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

    private static PacketMemory AdjustBuffer(PacketMemory memory)
    {
        var packetLength = GetPacketLength(memory.Memory.Span);
        if (packetLength < 20)
            throw new ArgumentException("Invalid IPv4 packet length.", nameof(memory));

        if (packetLength > memory.Memory.Length)
            throw new ArgumentException("Buffer too small for IPv4 packet.", nameof(memory));

        return memory.Slice(packetLength);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpV4Packet"/> class by parsing the packet at the start of some
    /// memory.
    /// </summary>
    /// <remarks>
    /// The packet takes the memory's first total-length bytes and leaves the rest. It checks the header's
    /// version and lengths, not its checksum.
    /// </remarks>
    /// <param name="memory">
    /// The memory, which holds the packet from its first byte and may be longer: a <see cref="Memory{T}"/> or an array
    /// the caller keeps, or memory given to the packet (see <see cref="PacketMemory"/>), which stays the caller's when
    /// parsing fails.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The bytes are no IPv4 packet, their header length or total length is invalid, or the memory is shorter
    /// than the packet.
    /// </exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="memory"/> is empty.</exception>
    public IpV4Packet(PacketMemory memory)
        : base(AdjustBuffer(memory))
    {
        var buffer = Buffer;

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
}

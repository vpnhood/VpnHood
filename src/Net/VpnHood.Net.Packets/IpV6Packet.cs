using System.Buffers;
using System.Buffers.Binary;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

/// <summary>
/// Represents an IPv6 packet (RFC 8200) over a buffer, which it reads and writes in place.
/// </summary>
/// <remarks>
/// <para>
/// A constructor that takes a <see cref="Memory{T}"/> leaves the memory to the caller; one that takes an
/// <see cref="IMemoryOwner{T}"/> owns it and disposes it with the packet.
/// </para>
/// <para>
/// A parsed packet reads through its Hop-by-Hop Options, Destination Options, Fragment and Authentication
/// headers to the upper layer, as Linux does. It stops at a Routing header and at a header that runs past the
/// packet: then <see cref="Protocol"/> names that header and <see cref="HasUnreadExtensionHeader"/> is
/// <see langword="true"/>. ESP, Mobility, HIP and Shim6 end the chain as protocols of their own:
/// <see cref="Protocol"/> names them, and <see cref="HasUnreadExtensionHeader"/> is <see langword="false"/>. A fragment
/// other than the first stops after its Fragment header, so its <see cref="IpPacket.Payload"/> holds fragment data.
/// <see cref="Header"/> covers the headers read through, and <see cref="IsFragment"/> counts only the Fragment
/// headers among them.
/// </para>
/// </remarks>
public class IpV6Packet : IpPacket
{
    private bool _disposed;
    private readonly IMemoryOwner<byte>? _memoryOwner;

    // set by the extension headers a parsed packet reads through, if it has any
    private readonly IpProtocol? _upperLayerProtocol;
    private readonly int _headerLength = 40;
    private readonly bool _isFragment;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpV6Packet"/> class by parsing the packet at the start of
    /// rented memory, which the packet then owns.
    /// </summary>
    /// <param name="memoryOwner">
    /// The memory, which holds the packet from its first byte and may be longer. The packet disposes it; when
    /// parsing fails, it stays the caller's.
    /// </param>
    /// <exception cref="ArgumentException">The bytes are no valid IPv6 packet, or the memory is shorter than the packet.</exception>
    /// <exception cref="IndexOutOfRangeException">The memory is empty.</exception>
    public IpV6Packet(IMemoryOwner<byte> memoryOwner)
        : this(memoryOwner.Memory)
    {
        _memoryOwner = memoryOwner;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpV6Packet"/> class by building a new packet in rented
    /// memory, which the packet then owns.
    /// </summary>
    /// <param name="memoryOwner">The memory, at least <paramref name="packetLength"/> bytes. The packet disposes it.</param>
    /// <param name="packetLength">The packet's total length in bytes, from 40 to 65575.</param>
    /// <param name="protocol">The Next Header: the protocol that follows the fixed header.</param>
    /// <exception cref="ArgumentException"><paramref name="packetLength"/> is below 40.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="packetLength"/> exceeds the memory.</exception>
    public IpV6Packet(IMemoryOwner<byte> memoryOwner, int packetLength, IpProtocol protocol)
        : this(memoryOwner.Memory[..packetLength], protocol)
    {
        _memoryOwner = memoryOwner;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpV6Packet"/> class by building a new packet in a buffer.
    /// </summary>
    /// <remarks>
    /// The whole buffer is cleared, then the version, the Next Header and the payload length are written. The
    /// addresses and the hop limit are left zero.
    /// </remarks>
    /// <param name="buffer">The buffer, exactly as long as the packet: from 40 to 65575 bytes.</param>
    /// <param name="protocol">The Next Header: the protocol that follows the fixed header.</param>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> is shorter than 40 bytes.</exception>
    public IpV6Packet(Memory<byte> buffer, IpProtocol protocol) : base(buffer)
    {
        // Check if the buffer is large enough for the IP header
        if (buffer.Length < 40)
            throw new ArgumentException("Buffer too small for IPv6 header.");

        // clean buffer
        buffer.Span.Clear();

        // set version
        Version = IpVersion.IPv6;

        // set protocol
        Span[6] = (byte)protocol;

        // set payload length
        var payloadLength = (ushort)(buffer.Length - 40);
        BinaryPrimitives.WriteUInt16BigEndian(Span.Slice(4, 2), payloadLength);
    }

    private static Memory<byte> AdjustBuffer(Memory<byte> buffer)
    {
        var packetLength = GetPacketLength(buffer.Span);
        if (packetLength < 40)
            throw new ArgumentException("Invalid IPv6 packet length.", nameof(buffer));

        if (packetLength > buffer.Length)
            throw new ArgumentException("Buffer too small for IPv6 packet.", nameof(buffer));
        return buffer[..packetLength];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpV6Packet"/> class by parsing the packet at the start of a
    /// buffer.
    /// </summary>
    /// <remarks>
    /// The packet takes the buffer's first 40 + payload-length bytes and leaves the rest.
    /// </remarks>
    /// <param name="buffer">The buffer, which holds the packet from its first byte and may be longer.</param>
    /// <exception cref="ArgumentException">The bytes are no IPv6 packet, or the buffer is shorter than the packet.</exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="buffer"/> is empty.</exception>
    public IpV6Packet(Memory<byte> buffer)
        : base(AdjustBuffer(buffer))
    {
        buffer = Buffer;

        // Check if the payment length is valid
        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Span.Slice(4, 2));
        if (buffer.Length != 40 + payloadLength)
            throw new ArgumentException("Buffer length does not match IPv6 payload length field.");

        // a packet without extension headers pays this one check
        if (IpV6ExtensionHeaders.IsExtensionHeader(NextHeader))
            _upperLayerProtocol =
                IpV6ExtensionHeaders.FindUpperLayer(buffer.Span, out _headerLength, out _isFragment);
    }

    private Span<byte> Span => Buffer.Span;

    /// <inheritdoc/>
    public override IpProtocol Protocol => _upperLayerProtocol ?? NextHeader;

    /// <summary>
    /// Gets a value indicating whether the packet is a fragment: it has a Fragment header whose offset is not
    /// zero or whose More Fragments flag is set. A packet built with the constructor is never one.
    /// </summary>
    public override bool IsFragment => _isFragment;

    /// <summary>
    /// Gets the Next Header field of the fixed header: the first extension header or the upper layer.
    /// </summary>
    public IpProtocol NextHeader => (IpProtocol)Span[6];

    /// <summary>
    /// Gets the fixed header and the extension headers read through; 40 bytes for a built packet.
    /// </summary>
    public override Memory<byte> Header => Buffer[.._headerLength];

    /// <summary>
    /// Gets a value indicating whether reading stopped at an extension header, which <see cref="Protocol"/> then
    /// names instead of the upper layer.
    /// </summary>
    public bool HasUnreadExtensionHeader => IpV6ExtensionHeaders.IsExtensionHeader(Protocol);

    /// <summary>
    /// Gets or sets the hop limit. The same as <see cref="HopLimit"/>.
    /// </summary>
    public override byte TimeToLive {
        get => HopLimit;
        set => HopLimit = value;
    }

    /// <summary>
    /// Gets or sets the hop limit: the number of hops the packet may still take.
    /// </summary>
    public byte HopLimit {
        get => Span[7];
        set => Span[7] = value;
    }

    private protected override Span<byte> SourceAddressBuffer {
        get => Span.Slice(8, 16);
        set {
            value.CopyTo(Span.Slice(8, 16));
            SourceAddressField = null;
        }
    }

    private protected override Span<byte> DestinationAddressBuffer {
        get => Span.Slice(24, 16);
        set {
            value.CopyTo(Span.Slice(24, 16));
            DestinationAddressField = null;
        }
    }

    /// <summary>
    /// Gets or sets the 20-bit flow label, from 0 to 1048575.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or above 1048575.</exception>
    public int FlowLabel {
        get {
            // Extract 20-bit Flow Label: lower 4 bits of Span[1], all Span[2] and Span[3]
            return ((Span[1] & 0x0F) << 16) | (Span[2] << 8) | Span[3];
        }
        set {
            if (value is < 0 or > 0xFFFFF)
                throw new ArgumentOutOfRangeException(nameof(value),
                    "Flow Label must be a 20-bit unsigned value (0 to 1048575).");

            // Write back 20-bit Flow Label
            Span[1] = (byte)((Span[1] & 0xF0) | ((value >> 16) & 0x0F));
            Span[2] = (byte)((value >> 8) & 0xFF);
            Span[3] = (byte)(value & 0xFF);
        }
    }

    /// <summary>
    /// Gets or sets the traffic class byte: the Differentiated Services Code Point in its high 6 bits and ECN in
    /// its low 2.
    /// </summary>
    public byte TrafficClass {
        get {
            // TrafficClass = low 4 bits of Span[0] and high 4 bits of Span[1]
            return (byte)(((Span[0] & 0x0F) << 4) | (Span[1] >> 4));
        }
        set {
            // value is 8 bits: upper 4 bits go to lower nibble of Span[0], lower 4 bits go to upper nibble of Span[1]
            Span[0] = (byte)((Span[0] & 0xF0) | (value >> 4)); // Preserve version (upper 4 bits)
            Span[1] = (byte)((Span[1] & 0x0F) | (value << 4)); // Preserve FlowLabel high bits
        }
    }

    /// <summary>
    /// Reads an IPv6 packet's total length from its header, without parsing the rest.
    /// </summary>
    /// <param name="buffer">The packet's first bytes, at least 6.</param>
    /// <returns>40 plus the payload length field: the packet's length in bytes.</returns>
    /// <exception cref="ArgumentException">The bytes are no IPv6 packet, or fewer than 6.</exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="buffer"/> is empty.</exception>
    public static int GetPacketLength(ReadOnlySpan<byte> buffer)
    {
        if (GetPacketVersion(buffer) != IpVersion.IPv6)
            throw new ArgumentException("Buffer is not an IPv6 packet.", nameof(buffer));

        if (buffer.Length < 6)
            throw new ArgumentException("IPv6 header requires at least 6 bytes to determine packet length.",
                nameof(buffer));

        var payloadLength = (ushort)((buffer[4] << 8) | buffer[5]);
        return 40 + payloadLength;
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

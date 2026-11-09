using System.Net;
using System.Text;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

/// <summary>
/// Represents an IPv4 or IPv6 packet over a buffer, which it reads and writes in place.
/// </summary>
/// <remarks>
/// <para>
/// The packet makes no copy: its fields are read from and written to the buffer it was made on, so a change
/// through the packet shows in the buffer. A change made to the buffer around the packet shows in the next
/// read, except in what the packet keeps: <see cref="SourceAddress"/> and <see cref="DestinationAddress"/>, made once,
/// and an IPv6 packet's extension-header layout, read when it is parsed. Make one with
/// <see cref="PacketBuilder"/>, or with the <see cref="IpV4Packet"/> and <see cref="IpV6Packet"/> constructors.
/// </para>
/// <para>
/// A packet made with an <see cref="System.Buffers.IMemoryOwner{T}"/> owns that memory, and <see cref="Dispose()"/>
/// returns it; then <see cref="Buffer"/> throws <see cref="ObjectDisposedException"/>, and every view taken from
/// the packet before, such as its <see cref="Payload"/> or a <see cref="TcpPacket"/>, must no longer be used. A
/// packet never disposed never returns its memory, which is left to the garbage collector. A packet made on a
/// <see cref="Memory{T}"/> leaves it to the caller. An instance is not thread-safe.
/// </para>
/// </remarks>
/// <param name="buffer">The packet's bytes, exactly as long as the packet.</param>
public abstract class IpPacket(Memory<byte> buffer) : IDisposable
{
    private bool _disposed;

    // the addresses as IPAddress, made once from the buffer and dropped when the buffer's address changes
    private IPAddress? _sourceAddress;
    private IPAddress? _destinationAddress;

    /// <summary>
    /// Gets the packet's bytes, header and payload.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The packet is disposed.</exception>
    public Memory<byte> Buffer => _disposed ? throw new ObjectDisposedException(nameof(IpPacket)) : buffer;

    /// <summary>
    /// Gets the packet's length in bytes, header included. It can be read after the packet is disposed.
    /// </summary>
    public int PacketLength => buffer.Length;

    /// <summary>
    /// Gets or sets the parsed form of the packet's payload, such as a <see cref="TcpPacket"/>, or
    /// <see langword="null"/> when none has been made.
    /// </summary>
    /// <remarks>
    /// The <c>Extract</c> and <c>Build</c> methods of <see cref="Extensions.IpPacketExtensions"/> set it and
    /// return it on later calls.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The value's buffer is not this packet's <see cref="Payload"/>.</exception>
    public IPayloadPacket? PayloadPacket {
        get;
        set => field = value == null || value.Buffer.Equals(Payload)
            ? value
            : throw new InvalidOperationException("The PayloadPacket buffer must match the packet buffer.");
    }

    /// <summary>
    /// Reads the IP version from a packet's first byte, without checking it.
    /// </summary>
    /// <param name="buffer">The packet's bytes.</param>
    /// <returns>The version field; a value other than 4 or 6 means the bytes are no IP packet.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="buffer"/> is empty.</exception>
    public static IpVersion GetPacketVersion(ReadOnlySpan<byte> buffer)
    {
        return (IpVersion)(buffer[0] >> 4);
    }

    /// <summary>
    /// Gets the packet's IP version.
    /// </summary>
    public IpVersion Version {
        get { return (IpVersion)(Buffer.Span[0] >> 4); }
        private protected set => Buffer.Span[0] = (byte)(((byte)value << 4) | (Buffer.Span[0] & 0x0F));
    }

    /// <summary>
    /// Gets whether the packet is an IPv4 packet.
    /// </summary>
    /// <value><see langword="true"/> if the packet is IPv4; otherwise, <see langword="false"/>.</value>
    public bool IsV4 => Version == IpVersion.IPv4;

    /// <summary>
    /// Gets whether the packet is an IPv6 packet.
    /// </summary>
    /// <value><see langword="true"/> if the packet is IPv6; otherwise, <see langword="false"/>.</value>
    public bool IsV6 => Version == IpVersion.IPv6;

    // the addresses in the header
    private protected abstract Span<byte> SourceAddressBuffer { get; }
    private protected abstract Span<byte> DestinationAddressBuffer { get; }

    /// <summary>
    /// Gets or sets the source address as the header's bytes: 4 for IPv4, 16 for IPv6.
    /// </summary>
    /// <remarks>
    /// The getter returns a view of the header, not a copy.
    /// </remarks>
    /// <exception cref="ArgumentException">The value's length is not that of the packet's IP version.</exception>
    public ReadOnlySpan<byte> SourceAddressSpan {
        get => SourceAddressBuffer;
        set {
            var buffer = SourceAddressBuffer;
            ThrowIfOtherVersion(value.Length == 4, value.Length == 16, buffer.Length, nameof(value));
            value.CopyTo(buffer);
            _sourceAddress = null;
        }
    }

    /// <summary>
    /// Gets or sets the destination address as the header's bytes: 4 for IPv4, 16 for IPv6.
    /// </summary>
    /// <remarks>
    /// The getter returns a view of the header, not a copy.
    /// </remarks>
    /// <exception cref="ArgumentException">The value's length is not that of the packet's IP version.</exception>
    public ReadOnlySpan<byte> DestinationAddressSpan {
        get => DestinationAddressBuffer;
        set {
            var buffer = DestinationAddressBuffer;
            ThrowIfOtherVersion(value.Length == 4, value.Length == 16, buffer.Length, nameof(value));
            value.CopyTo(buffer);
            _destinationAddress = null;
        }
    }

    /// <summary>
    /// Gets or sets the source address.
    /// </summary>
    /// <remarks>
    /// The getter makes the <see cref="IPAddress"/> once and returns the same instance until the address
    /// changes through this packet. Code that runs per packet reads <see cref="SourceAddressValue"/>, which makes no
    /// object.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value is not of the packet's IP version.</exception>
    public IPAddress SourceAddress {
        get => _sourceAddress ??= new IPAddress(SourceAddressSpan);
        set {
            ArgumentNullException.ThrowIfNull(value);
            var buffer = SourceAddressBuffer;
            ThrowIfOtherVersion(value.IsV4, value.IsV6, buffer.Length, nameof(value));
            value.GetAddressBytesFast(buffer);
            _sourceAddress = value;
        }
    }

    /// <summary>
    /// Gets or sets the destination address.
    /// </summary>
    /// <remarks>
    /// The getter makes the <see cref="IPAddress"/> once and returns the same instance until the address
    /// changes through this packet. Code that runs per packet reads <see cref="DestinationAddressValue"/>, which makes
    /// no object.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value is not of the packet's IP version.</exception>
    public IPAddress DestinationAddress {
        get => _destinationAddress ??= new IPAddress(DestinationAddressSpan);
        set {
            ArgumentNullException.ThrowIfNull(value);
            var buffer = DestinationAddressBuffer;
            ThrowIfOtherVersion(value.IsV4, value.IsV6, buffer.Length, nameof(value));
            value.GetAddressBytesFast(buffer);
            _destinationAddress = value;
        }
    }

    /// <summary>
    /// Gets or sets the source address as a value.
    /// </summary>
    /// <remarks>
    /// The getter makes no object: it reads the header, or the <see cref="IPAddress"/> the packet keeps for
    /// <see cref="SourceAddress"/>, which carries the scope id a set gave it. A set writes the address's bytes; a
    /// header has no room for a scope id, so a scoped address is kept as that <see cref="IPAddress"/>, as the
    /// <see cref="SourceAddress"/> setter keeps its value, and an address without one drops the kept address.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not of the packet's IP version, or holds no address.</exception>
    public IpAddressValue SourceAddressValue {
        get => _sourceAddress?.ToValue() ?? new IpAddressValue(SourceAddressBuffer);
        set {
            var buffer = SourceAddressBuffer;
            ThrowIfOtherVersion(value.IsV4, value.IsV6, buffer.Length, nameof(value));
            _ = value.TryWriteBytes(buffer, out _);
            _sourceAddress = value.ScopeId == 0 ? null : value.ToIPAddress();
        }
    }

    /// <summary>
    /// Gets or sets the destination address as a value.
    /// </summary>
    /// <remarks>
    /// The getter makes no object: it reads the header, or the <see cref="IPAddress"/> the packet keeps for
    /// <see cref="DestinationAddress"/>, which carries the scope id a set gave it. A set writes the address's bytes; a
    /// header has no room for a scope id, so a scoped address is kept as that <see cref="IPAddress"/>, as the
    /// <see cref="DestinationAddress"/> setter keeps its value, and an address without one drops the kept address.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not of the packet's IP version, or holds no address.</exception>
    public IpAddressValue DestinationAddressValue {
        get => _destinationAddress?.ToValue() ?? new IpAddressValue(DestinationAddressBuffer);
        set {
            var buffer = DestinationAddressBuffer;
            ThrowIfOtherVersion(value.IsV4, value.IsV6, buffer.Length, nameof(value));
            _ = value.TryWriteBytes(buffer, out _);
            _destinationAddress = value.ScopeId == 0 ? null : value.ToIPAddress();
        }
    }

    /// <summary>
    /// Gets the protocol of the upper layer the payload carries, such as TCP.
    /// </summary>
    /// <remarks>
    /// An IPv6 packet reads through its extension headers to the upper layer; when it stops at one it does not
    /// read through, it names that header instead (see <see cref="IpV6Packet.HasUnreadExtensionHeader"/>).
    /// </remarks>
    public abstract IpProtocol Protocol { get; }

    /// <summary>
    /// Gets a value indicating whether the packet is a fragment: a piece of a datagram, whose upper-layer packet
    /// no single piece holds whole.
    /// </summary>
    public abstract bool IsFragment { get; }

    /// <summary>
    /// Gets or sets the number of hops the packet may still take: IPv4's time to live, IPv6's hop limit.
    /// </summary>
    public abstract byte TimeToLive { get; set; }

    /// <summary>
    /// Gets the bytes before the upper layer: the IP header with its options, and IPv6's extension headers.
    /// </summary>
    public abstract Memory<byte> Header { get; }

    /// <summary>
    /// Gets the bytes after <see cref="Header"/>: the upper-layer packet, such as a TCP segment.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The packet is disposed.</exception>
    public Memory<byte> Payload => Buffer[Header.Length..];

    /// <summary>
    /// Returns a one-line description of the packet for logs: its addresses, protocol and lengths.
    /// </summary>
    /// <returns>The description; for a disposed packet, only its length.</returns>
    public override string ToString()
    {
        // a disposed packet's buffer is gone; error paths still log a packet another owner has disposed
        if (_disposed)
            return $"Disposed, TotalLength:{PacketLength}";

        var builder = new StringBuilder();
        builder.Append($"Src={SourceAddress}, Dst={DestinationAddress}, Proto={Protocol}, ");
        builder.Append($"TotalLength:{Buffer.Length}, PayloadLen={Payload.Length}");

        if (PayloadPacket != null)
            builder.Append($", PayloadPacket: {PayloadPacket.GetType().Name}, {PayloadPacket}");

        return builder.ToString();
    }

    // An address of the other version, or none, would be written over part of the header's address and leave the
    // packet's kept IPAddress stale; it is refused before anything is written. The length of the header's address tells
    // the packet's version: reading Version instead would read the buffer once more on each set.
    private static void ThrowIfOtherVersion(bool isV4, bool isV6, int headerAddressLength, string paramName)
    {
        if (headerAddressLength != (isV4 ? 4 : isV6 ? 16 : 0))
            throw new ArgumentException("The address is not of the packet's IP version.", paramName);
    }

    /// <summary>
    /// Marks the packet disposed. A derived class returns the memory it owns here.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release
    /// only unmanaged resources.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        _ = disposing;
        _disposed = true;
    }

    // Add no finalizer: every packet would pay for it as it is made, and it could return an array a view of the
    // packet still uses. An undisposed packet's array goes to the GC; the pool keeps no record of what it lends
    /// <summary>
    /// Disposes the packet and returns the memory it owns, if any. Its buffer must not be used afterwards.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
    }
}

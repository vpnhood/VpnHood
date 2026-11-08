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
    private protected IPAddress? SourceAddressField;
    private protected IPAddress? DestinationAddressField;

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

    // the addresses in the buffer; a set copies the value in and drops the cached IPAddress
    private protected abstract Span<byte> SourceAddressBuffer { get; set; }
    private protected abstract Span<byte> DestinationAddressBuffer { get; set; }

    /// <summary>
    /// Gets or sets the source address as the header's bytes: 4 for IPv4, 16 for IPv6.
    /// </summary>
    /// <remarks>
    /// The getter returns a view of the header, not a copy.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is longer than the header's address.</exception>
    public ReadOnlySpan<byte> SourceAddressSpan {
        get => SourceAddressBuffer;
        set {
            if (!value.TryCopyTo(SourceAddressBuffer))
                throw new ArgumentException("Invalid IP address format.");
            SourceAddressBuffer = null;
        }
    }

    /// <summary>
    /// Gets or sets the destination address as the header's bytes: 4 for IPv4, 16 for IPv6.
    /// </summary>
    /// <remarks>
    /// The getter returns a view of the header, not a copy.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is longer than the header's address.</exception>
    public ReadOnlySpan<byte> DestinationAddressSpan {
        get => DestinationAddressBuffer;
        set {
            if (!value.TryCopyTo(DestinationAddressBuffer))
                throw new ArgumentException("Invalid IP address format.");
            DestinationAddressBuffer = null;
        }
    }

    /// <summary>
    /// Gets or sets the source address.
    /// </summary>
    /// <remarks>
    /// The getter makes the <see cref="IPAddress"/> once and returns the same instance until the address
    /// changes through this packet.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not of the packet's IP version.</exception>
    public IPAddress SourceAddress {
        get => SourceAddressField ??= new IPAddress(SourceAddressSpan);
        set {
            if (!value.TryWriteBytes(SourceAddressBuffer, out var written) || written != SourceAddressSpan.Length)
                throw new ArgumentException("Invalid IP address format.");
            SourceAddressField = value;
        }
    }

    /// <summary>
    /// Gets or sets the destination address.
    /// </summary>
    /// <remarks>
    /// The getter makes the <see cref="IPAddress"/> once and returns the same instance until the address
    /// changes through this packet.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not of the packet's IP version.</exception>
    public IPAddress DestinationAddress {
        get => DestinationAddressField ??= new IPAddress(DestinationAddressSpan);
        set {
            if (!value.TryWriteBytes(DestinationAddressBuffer, out var written) ||
                written != DestinationAddressSpan.Length)
                throw new ArgumentException("Invalid IP address format.");
            DestinationAddressField = value;
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

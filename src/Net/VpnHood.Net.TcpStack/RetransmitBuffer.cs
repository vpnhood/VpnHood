using System.Runtime.CompilerServices;

namespace VpnHood.Net.TcpStack;

/// <summary>
/// The bytes a connection sent and the peer has not acknowledged, from SndUna on, kept for fast retransmit and the RTO:
/// a ring of RetransmitBufferSize bytes whose offset k holds the byte at sequence SndUna + k, allocated on the first
/// send so a receive-only connection never pays for it. Used under its connection's lock; not thread-safe on its own.
/// A class, not a struct: in the connection's readonly field a struct's changes would land on copies.
/// </summary>
internal sealed class RetransmitBuffer(int capacity)
{
    private readonly int _capacity = capacity;
    private byte[]? _buffer;
    private int _start; // the index of the byte at SndUna

    /// <summary>The unacknowledged bytes.</summary>
    public int Length { get; private set; }

    public int Free => _capacity - Length;

    /// <summary>Appends the segment being sent at SndNxt. It must fit in <see cref="Free"/>, which the senders keep to: an
    /// overflow would wrap over unacknowledged bytes and corrupt what is retransmitted, so it is refused instead.</summary>
    public void Append(ReadOnlySpan<byte> segment)
    {
        if (segment.Length == 0) return;
        if (segment.Length > Free)
            throw new InvalidOperationException($"The retransmit buffer has {Free} bytes free for a segment of {segment.Length}.");
        _buffer ??= new byte[_capacity];
        var writeIndex = (_start + Length) % _capacity;
        var firstChunk = Math.Min(segment.Length, _capacity - writeIndex);
        segment[..firstChunk].CopyTo(_buffer.AsSpan(writeIndex));
        if (firstChunk < segment.Length)
            segment[firstChunk..].CopyTo(_buffer.AsSpan(0));
        Length += segment.Length;
    }

    /// <summary>Drops the acknowledged bytes from the front. An ack past the end (the FIN's sequence number is not in the
    /// buffer) empties it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // per ACK
    public void Trim(int acked)
    {
        if (acked >= Length) {
            Length = 0;
            _start = 0;
        }
        else {
            _start = (_start + acked) % _capacity;
            Length -= acked;
        }
    }

    /// <summary>Copies the segment at SndUna + offset, up to maxLength bytes, for a retransmission.</summary>
    public byte[] CopyAt(int offset, int maxLength)
    {
        var length = Math.Min(Length - offset, maxLength);
        if (length <= 0) return [];
        var buffer = _buffer ?? throw new InvalidOperationException("The retransmit buffer holds bytes but was never allocated.");
        var copy = new byte[length];
        var readIndex = (_start + offset) % _capacity;
        var firstChunk = Math.Min(length, _capacity - readIndex);
        Array.Copy(buffer, readIndex, copy, 0, firstChunk);
        if (firstChunk < length)
            Array.Copy(buffer, 0, copy, firstChunk, length - firstChunk);
        return copy;
    }
}

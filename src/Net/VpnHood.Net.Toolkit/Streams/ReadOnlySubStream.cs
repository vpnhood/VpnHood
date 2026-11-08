using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Streams;

/// <summary>
/// Provides a read-only, seekable window onto part of another stream: positions start at 0 at the window's start, and the
/// window's end is the stream's end. It reads in place, with no copy, as for an uncompressed entry inside an Android package.
/// </summary>
/// <remarks>
/// Each nonempty read before the window's end positions the source first; seeking moves only the window's position. So
/// one source must not serve two windows at once, and a window must not be read concurrently. Writing is never
/// supported; reading, seeking and read timeouts are the source's.
/// </remarks>
/// <param name="source">The source stream, which must read and seek.</param>
/// <param name="start">The window's start in the source.</param>
/// <param name="length">The window's length, which must end within the source.</param>
/// <param name="leaveOpen"><see langword="true"/> to leave the source open when the window is disposed.</param>
/// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
/// <exception cref="ArgumentException">The source cannot read or seek.</exception>
/// <exception cref="ArgumentOutOfRangeException">
/// <paramref name="start"/> or <paramref name="length"/> is negative, or the window ends past the source's end.
/// </exception>
public class ReadOnlySubStream(Stream source, long start, long length, bool leaveOpen = false) : Stream
{
    private readonly Stream _source = Validate(source, start, length);
    private long _position;
    private bool _disposed;

    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override bool CanRead => _source.CanRead;
    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override bool CanSeek => _source.CanSeek;
    /// <inheritdoc/>
    /// <remarks>It is always <see langword="false"/>.</remarks>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override bool CanTimeout => _source.CanTimeout;
    /// <inheritdoc/>
    /// <remarks>The window's length.</remarks>
    public override long Length => length;

    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override int ReadTimeout {
        get => _source.ReadTimeout;
        set => _source.ReadTimeout = value;
    }

    /// <inheritdoc/>
    /// <remarks>The position in the window; setting it seeks from the window's start.</remarks>
    public override long Position {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    /// <remarks>It reads no further than the window's end.</remarks>
    public override int Read(Span<byte> buffer)
    {
        var count = Remaining(buffer.Length);
        if (count == 0)
            return 0;

        _source.Position = start + _position;
        var read = _source.Read(buffer[..count]);
        _position += read;
        return read;
    }

    /// <inheritdoc/>
    // Allocation-free: Stream's own ReadByte makes a one-byte array per call, and an archive reader
    // calls it a great many times.
    public override int ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        return Read(buffer) == 1 ? buffer[0] : -1;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    /// <remarks>It reads no further than the window's end.</remarks>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var count = Remaining(buffer.Length);
        if (count == 0)
            return 0;

        _source.Position = start + _position;
        var read = await _source.ReadAsync(buffer[..count], cancellationToken).Vhc();
        _position += read;
        return read;
    }

    /// <summary>Sets the position in the window; past its end is allowed, where a read returns 0.</summary>
    /// <param name="position">The offset from the origin.</param>
    /// <param name="origin">The origin: the window's start, the current position, or the window's end.</param>
    /// <returns>The new position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position would be before the window's start, or the origin is not valid.</exception>
    public override long Seek(long position, SeekOrigin origin)
    {
        var target = origin switch {
            SeekOrigin.Begin => position,
            SeekOrigin.Current => _position + position,
            SeekOrigin.End => length + position,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };

        // past the end is allowed, as it is on any seekable stream - a read there simply returns 0.
        // Only a position before the start is an error.
        ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(position));

        _position = target;
        return _position;
    }

    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override void Flush()
    {
        _source.Flush();
    }

    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _source.FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

    // How much of a read of this size the window still has room for.
    private int Remaining(int count)
    {
        return (int)Math.Min(count, Math.Max(0, length - _position));
    }

    // The window has to be a window of something readable and seekable, and has to lie inside it:
    // a range running past the end of the source would read short at some unrelated later moment.
    // ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local
    private static Stream Validate(Stream source, long start, long length)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("A window needs a source that both reads and seeks.", nameof(source));

        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + length, source.Length, nameof(length));
        return source;
    }
    // ReSharper restore ParameterOnlyUsedForPreconditionCheck.Local

    /// <summary>Closes the source, once, unless left open, when <paramref name="disposing"/>.</summary>
    /// <param name="disposing"><see langword="true"/> for the synchronous close.</param>
    // The flag releases the source once and once only, whichever way this was disposed.
    protected override void Dispose(bool disposing)
    {
        if (disposing && !Interlocked.Exchange(ref _disposed, true) && !leaveOpen)
            _source.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>Closes the source asynchronously, once, unless left open, then disposes the window.</summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    // Stream's own DisposeAsync falls back to the synchronous Dispose, which blocks on a source
    // that has real work to do (a FileStream's flush and handle release). So the source's async
    // close, then Dispose(false), as Stream's pattern has it.
    public override async ValueTask DisposeAsync()
    {
        if (!Interlocked.Exchange(ref _disposed, true) && !leaveOpen)
            await _source.DisposeAsync().Vhc();

        Dispose(false);
        GC.SuppressFinalize(this);
    }
}

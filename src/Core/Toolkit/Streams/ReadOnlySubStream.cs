using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Toolkit.Streams;

// A window onto part of another stream, read-only and seekable: positions start at 0 at the
// window's start, and the end of the window is the end of the stream.
//
// What it is for: a file that lies INSIDE another file and has to be read as if it stood alone -
// an uncompressed entry of an Android package, which is a byte range of the .apk. The bytes are
// read where they are, with no copy anywhere.
//
// Every read hands the caller's own buffer to the source, sliced to what is left of the window -
// a span or a memory slice costs nothing and copies nothing. The array overloads are the same call
// over AsSpan/AsMemory, and both the sync and the async paths are overridden, because Stream's own
// defaults for the ones left out rent a pooled array and copy through it.
//
// What it can do is what the source can do, asked of the source rather than asserted here - except
// writing, which this never does whatever the source allows. A source that cannot read or seek
// cannot be a window at all, so that is refused up front rather than at the first read.
//
// The source is positioned on every read, so one source must not be shared by two of these at
// once, and one of these must not be read concurrently with itself - the same rule its readers
// already follow (a ZipArchive is not thread-safe either).
public class ReadOnlySubStream(Stream source, long start, long length, bool leaveOpen = false) : Stream
{
    private readonly Stream _source = Validate(source, start, length);
    private long _position;
    private bool _disposed;

    public override bool CanRead => _source.CanRead;
    public override bool CanSeek => _source.CanSeek;
    public override bool CanWrite => false;
    public override bool CanTimeout => _source.CanTimeout;
    public override long Length => length;

    public override int ReadTimeout {
        get => _source.ReadTimeout;
        set => _source.ReadTimeout = value;
    }

    public override long Position {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

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

    // Allocation-free: Stream's own ReadByte makes a one-byte array per call, and an archive reader
    // calls it a great many times.
    public override int ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        return Read(buffer) == 1 ? buffer[0] : -1;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

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

    public override void Flush()
    {
        _source.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _source.FlushAsync(cancellationToken);
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

    // How much of a read of this size the window still has room for.
    private int Remaining(int count)
    {
        return (int)Math.Min(count, Math.Max(0, length - _position));
    }

    // The window has to be a window of something readable and seekable, and has to lie inside it:
    // a range running past the end of the source would read short at some unrelated later moment.
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

    // Both paths end in Stream's Dispose (DisposeAsync's base falls through to it), so the source is
    // released once and once only, whichever way this was disposed.
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) {
            _disposed = true;
            if (!leaveOpen)
                _source.Dispose();
        }

        base.Dispose(disposing);
    }

    // Stream's own DisposeAsync falls back to the synchronous Dispose, which blocks on a source
    // that has real work to do (a FileStream's flush and handle release).
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed) {
            _disposed = true;
            if (!leaveOpen)
                await _source.DisposeAsync().Vhc();
        }

        await base.DisposeAsync().Vhc();
    }
}

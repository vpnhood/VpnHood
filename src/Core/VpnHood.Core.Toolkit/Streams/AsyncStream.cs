namespace VpnHood.Core.Toolkit.Streams;

/// <summary>
/// Base <see cref="Stream"/> for async-only streams. It centralizes the repetitive plumbing:
/// every synchronous I/O member throws <see cref="NotSupportedException"/>, and the legacy
/// byte[]-based async overloads are routed to the <see cref="Memory{T}"/>-based core methods.
/// Derived classes only implement the real I/O: <see cref="ReadAsync(Memory{byte}, CancellationToken)"/>,
/// <see cref="WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/>, <see cref="CanRead"/> and <see cref="CanWrite"/>.
/// </summary>
public abstract class AsyncStream : Stream
{
    // ---- core functionality: derived classes implement these ----
    public abstract override bool CanRead { get; }
    public abstract override bool CanWrite { get; }
    public abstract override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    public abstract override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    // ---- byte[] async overloads are routed to the Memory-based core ----
    public sealed override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public sealed override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ---- seek/length: not supported by default; decorators may override to forward ----
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();

    public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    // ---- synchronous I/O is not supported: use the async members ----
    public sealed override void Flush() => 
        throw new NotSupportedException("Use FlushAsync.");

    public sealed override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Use ReadAsync.");

    public sealed override int Read(Span<byte> buffer) =>
        throw new NotSupportedException("Use ReadAsync.");

    public sealed override int ReadByte() =>
        throw new NotSupportedException("Use ReadAsync.");

    public sealed override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Use WriteAsync.");

    public sealed override void Write(ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException("Use WriteAsync.");

    public sealed override void WriteByte(byte value) =>
        throw new NotSupportedException("Use WriteAsync.");

    public sealed override void CopyTo(Stream destination, int bufferSize) =>
        throw new NotSupportedException("Use CopyToAsync.");

    public sealed override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback,
        object? state) => throw new NotSupportedException("Use ReadAsync.");

    public sealed override int EndRead(IAsyncResult asyncResult) =>
        throw new NotSupportedException("Use ReadAsync.");

    public sealed override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback,
        object? state) => throw new NotSupportedException("Use WriteAsync.");

    public sealed override void EndWrite(IAsyncResult asyncResult) =>
        throw new NotSupportedException("Use WriteAsync.");
}

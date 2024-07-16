namespace VpnHood.Common.Utils;

public class AsyncStreamDecorator<T>(T sourceStream, bool leaveOpen) : Stream
    where T : Stream
{
    protected T SourceStream = sourceStream;

    public override bool CanRead => SourceStream.CanRead;
    public override bool CanSeek => SourceStream.CanSeek;
    public override bool CanWrite => SourceStream.CanWrite;
    public override long Length => SourceStream.Length;
    public override bool CanTimeout => SourceStream.CanTimeout;

    public override long Position {
        get => SourceStream.Position;
        set {
            if (!CanSeek)
                throw new NotSupportedException();

            SourceStream.Position = value;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!CanSeek)
            throw new NotSupportedException();

        return SourceStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        SourceStream.SetLength(value);
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return SourceStream.FlushAsync(cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return SourceStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return SourceStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public sealed override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        return base.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    public sealed override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return base.ReadAsync(buffer, cancellationToken);
    }

    public override ValueTask DisposeAsync()
    {
        return leaveOpen ? default : SourceStream.DisposeAsync();
    }

    // Sealed
    public sealed override int WriteTimeout {
        get => SourceStream.WriteTimeout;
        set => SourceStream.WriteTimeout = value;
    }

    public sealed override int ReadTimeout {
        get => SourceStream.ReadTimeout;
        set => SourceStream.ReadTimeout = value;
    }

    public sealed override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        return base.WriteAsync(buffer, cancellationToken);
    }

    public sealed override int ReadByte()
    {
        throw new NotSupportedException("Use ReadAsync.");
    }

    public sealed override void Close()
    {
        throw new NotSupportedException("Use DisposeAsync.");
    }

    protected sealed override void Dispose(bool disposing)
    {
        throw new NotSupportedException("Use DisposeAsync.");
    }

    public sealed override void WriteByte(byte value)
    {
        throw new NotSupportedException("Use WriteAsync.");
    }

    public sealed override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Use WriteAsync.");
    }

    public sealed override void Write(ReadOnlySpan<byte> buffer)
    {
        throw new NotSupportedException("Use WriteAsync.");
    }

    public sealed override void Flush()
    {
        throw new NotSupportedException("Use FlushAsync.");
    }

    public sealed override void CopyTo(Stream destination, int bufferSize)
    {
        throw new NotSupportedException("Use CopyToAsync.");
    }

    public sealed override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Use ReadAsync.");
    }

    public sealed override int Read(Span<byte> buffer)
    {
        throw new NotSupportedException("Use ReadAsync.");
    }

    public sealed override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback,
        object state)
    {
        throw new NotSupportedException("Use ReadAsync.");
    }

    public sealed override int EndRead(IAsyncResult asyncResult)
    {
        throw new NotSupportedException("Use ReadAsync.");
    }

    public sealed override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback,
        object state)
    {
        throw new NotSupportedException("Use WriteAsync.");
    }

    public sealed override void EndWrite(IAsyncResult asyncResult)
    {
        throw new NotSupportedException("Use WriteAsync.");
    }
}

public class AsyncStreamDecorator(Stream sourceStream, bool leaveOpen)
    : AsyncStreamDecorator<Stream>(sourceStream, leaveOpen);
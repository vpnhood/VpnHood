namespace VpnHood.Core.Toolkit.Utils;

public class AsyncStreamDecorator<T>(T sourceStream, bool leaveOpen) : Stream
    where T : Stream
{
    protected readonly T SourceStream = sourceStream;
    protected bool IsDisposed { get; private set; }
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

    public sealed override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return SourceStream.ReadAsync(buffer, cancellationToken);
    }

    public sealed override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return SourceStream.WriteAsync(buffer, cancellationToken);
    }

    public sealed override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        return base.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!leaveOpen)
            await SourceStream.DisposeAsync();
        
        await base.DisposeAsync();

        // the base class does not call Dispose(bool) so we need to do it manually
        Dispose(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (disposing) {
            if (!leaveOpen)
                SourceStream.Dispose();
        }

        IsDisposed = true;
    }

    public sealed override void Close()
    {
        if (!leaveOpen)
            SourceStream.Close();
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

    public sealed override int ReadByte()
    {
        throw new NotSupportedException("Use ReadAsync.");
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
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.Common.Utils;

public class AsyncStreamDecorator<T> : Stream where T : Stream
{
    protected T OriginalStream;

    public AsyncStreamDecorator(T stream)
    {
        OriginalStream = stream;
    }

    public override bool CanRead => OriginalStream.CanRead;
    public override bool CanSeek => OriginalStream.CanSeek;
    public override bool CanWrite => OriginalStream.CanWrite;
    public override long Length => OriginalStream.Length;
    public override bool CanTimeout => OriginalStream.CanTimeout;
    public override long Position
    {
        get => OriginalStream.Position;
        set => OriginalStream.Position = value;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return OriginalStream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        OriginalStream.SetLength(value);
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return OriginalStream.FlushAsync(cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return OriginalStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return OriginalStream.WriteAsync(buffer, offset, count, cancellationToken);
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
        return OriginalStream.DisposeAsync();
    }

    // Sealed
    public sealed override int WriteTimeout
    {
        get => OriginalStream.WriteTimeout;
        set => OriginalStream.WriteTimeout = value;
    }

    public sealed override int ReadTimeout
    {
        get => OriginalStream.ReadTimeout;
        set => OriginalStream.ReadTimeout = value;
    }

    public sealed override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
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

    public sealed override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        throw new NotSupportedException("Use ReadAsync.");
    }

    public sealed override int EndRead(IAsyncResult asyncResult)
    {
        throw new NotSupportedException("Use ReadAsync.");
    }

    public sealed override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        throw new NotSupportedException("Use WriteAsync.");
    }

    public sealed override void EndWrite(IAsyncResult asyncResult)
    {
        throw new NotSupportedException("Use WriteAsync.");
    }
}

public class AsyncStreamDecorator : AsyncStreamDecorator<Stream>
{
    public AsyncStreamDecorator(Stream stream) : base(stream)
    {
    }
}

using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Streams;

public class AsyncStreamDecorator<T>(T sourceStream, bool leaveOpen)
    : AsyncStream, IDataStream where T : Stream
{
    protected readonly T SourceStream = sourceStream;
    protected bool IsDisposed { get; private set; }

    public override bool CanRead => SourceStream.CanRead;
    public override bool CanWrite => SourceStream.CanWrite;
    public override bool CanSeek => SourceStream.CanSeek;
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

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return SourceStream.ReadAsync(buffer, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return SourceStream.WriteAsync(buffer, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!leaveOpen)
            await SourceStream.DisposeAsync();

        await base.DisposeAsync();

        // the base class does not call Dispose(bool) so we need to do it manually
        // ReSharper disable once MethodHasAsyncOverload
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

    public virtual bool? DataAvailable => SourceStream switch {
        IDataStream dataStream => dataStream.DataAvailable,
        NetworkStream networkStream => networkStream.DataAvailable,
        _ => null
    };
}

public class StreamDecoratorAsync(Stream sourceStream, bool leaveOpen)
    : AsyncStreamDecorator<Stream>(sourceStream, leaveOpen);

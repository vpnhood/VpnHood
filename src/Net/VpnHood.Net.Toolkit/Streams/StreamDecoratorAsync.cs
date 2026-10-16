using System.Net.Sockets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Streams;

public class AsyncStreamDecorator<T>(T sourceStream, bool leaveOpen)
    : AsyncStream, IDataStream where T : Stream
{
    protected readonly T SourceStream = sourceStream;
    private bool _isSourceClosed;
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

    // the source closes once, by whichever close comes first, also when two run at once
    protected ValueTask CloseSourceAsync()
    {
        return Interlocked.Exchange(ref _isSourceClosed, true)
            ? ValueTask.CompletedTask
            : SourceStream.DisposeAsync();
    }

    // The async close, then Dispose(false), as Stream's pattern has it: Dispose(true) is the sync close's, and would close
    // the source again, synchronously, which waits for the peer on a QUIC stream
    public override async ValueTask DisposeAsync()
    {
        if (!leaveOpen)
            await CloseSourceAsync().Vhc();

        Dispose(false);
        GC.SuppressFinalize(this);
    }

    // the sync close's, through Stream's Close, as any stream's
    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (disposing && !leaveOpen && !Interlocked.Exchange(ref _isSourceClosed, true))
            SourceStream.Dispose();

        IsDisposed = true;
        base.Dispose(disposing);
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

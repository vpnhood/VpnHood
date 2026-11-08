using System.Net.Sockets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Streams;

/// <summary>
/// Provides a base for a stream that wraps another one: its asynchronous I/O, capabilities, length, position, seeking and
/// timeouts go to the source, and closing it closes the source, once, unless left open. Synchronous I/O is not supported,
/// as on any <see cref="AsyncStream"/>.
/// </summary>
/// <remarks>
/// Its <see cref="DisposeAsync"/> closes the source asynchronously: a synchronous close can block, as a QUIC stream's
/// waits for its peer.
/// </remarks>
/// <typeparam name="T">The source's type.</typeparam>
/// <param name="sourceStream">The source stream.</param>
/// <param name="leaveOpen"><see langword="true"/> to leave the source open when this stream is disposed.</param>
public class AsyncStreamDecorator<T>(T sourceStream, bool leaveOpen)
    : AsyncStream, IDataStream where T : Stream
{
    /// <summary>The source stream.</summary>
    protected readonly T SourceStream = sourceStream;
    private bool _isSourceClosed;
    /// <summary>Gets a value indicating whether the stream is disposed.</summary>
    protected bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override bool CanRead => SourceStream.CanRead;
    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override bool CanWrite => SourceStream.CanWrite;
    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override bool CanSeek => SourceStream.CanSeek;
    /// <inheritdoc cref="Stream.Length"/>
    /// <remarks>The source's.</remarks>
    public override long Length => SourceStream.Length;
    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override bool CanTimeout => SourceStream.CanTimeout;

    /// <inheritdoc cref="Stream.Position"/>
    /// <remarks>The source's.</remarks>
    public override long Position {
        get => SourceStream.Position;
        set {
            if (!CanSeek)
                throw new NotSupportedException();

            SourceStream.Position = value;
        }
    }

    /// <inheritdoc cref="Stream.Seek"/>
    /// <remarks>The source's.</remarks>
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!CanSeek)
            throw new NotSupportedException();

        return SourceStream.Seek(offset, origin);
    }

    /// <inheritdoc cref="Stream.SetLength"/>
    /// <remarks>The source's.</remarks>
    public override void SetLength(long value)
    {
        SourceStream.SetLength(value);
    }

    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return SourceStream.FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return SourceStream.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return SourceStream.WriteAsync(buffer, cancellationToken);
    }

    /// <summary>Closes the source asynchronously, once, by whichever close comes first.</summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected ValueTask CloseSourceAsync()
    {
        return Interlocked.Exchange(ref _isSourceClosed, true)
            ? ValueTask.CompletedTask
            : SourceStream.DisposeAsync();
    }

    /// <summary>Closes the source asynchronously, unless left open, then disposes this stream.</summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    // The async close, then Dispose(false), as Stream's pattern has it: Dispose(true) is the sync close's, and would close
    // the source again, synchronously, which waits for the peer on a QUIC stream
    public override async ValueTask DisposeAsync()
    {
        if (!leaveOpen)
            await CloseSourceAsync().Vhc();

        Dispose(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Closes the source synchronously, unless left open or closed already, when <paramref name="disposing"/>.</summary>
    /// <param name="disposing"><see langword="true"/> for the synchronous close.</param>
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
    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public sealed override int WriteTimeout {
        get => SourceStream.WriteTimeout;
        set => SourceStream.WriteTimeout = value;
    }

    /// <inheritdoc/>
    /// <remarks>The source's.</remarks>
    public sealed override int ReadTimeout {
        get => SourceStream.ReadTimeout;
        set => SourceStream.ReadTimeout = value;
    }

    /// <summary>
    /// Gets a value indicating whether data is waiting to be read: the source's, when it is an <see cref="IDataStream"/>
    /// or a <see cref="System.Net.Sockets.NetworkStream"/>; otherwise, <see langword="null"/>.
    /// </summary>
    public virtual bool? DataAvailable => SourceStream switch {
        IDataStream dataStream => dataStream.DataAvailable,
        NetworkStream networkStream => networkStream.DataAvailable,
        _ => null
    };
}

/// <summary>Provides an <see cref="AsyncStreamDecorator{T}"/> over any stream.</summary>
/// <param name="sourceStream">The source stream.</param>
/// <param name="leaveOpen"><see langword="true"/> to leave the source open when this stream is disposed.</param>
public class StreamDecoratorAsync(Stream sourceStream, bool leaveOpen)
    : AsyncStreamDecorator<Stream>(sourceStream, leaveOpen);

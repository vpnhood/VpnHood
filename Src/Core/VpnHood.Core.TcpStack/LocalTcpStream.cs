using System.Buffers;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.TcpStack;

/// <summary>
/// A standard .NET Stream implementation for TCP connections through the local TCP stack.
/// Provides async read/write operations over TCP connections using System.IO.Pipelines for efficiency.
/// </summary>
public sealed class LocalTcpStream : Stream
{
    private readonly LocalTcpConnection _connection;
    private readonly LocalTcpStack _stack;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    internal LocalTcpStream(LocalTcpConnection connection, LocalTcpStack stack)
    {
        _connection = connection;
        _stack = stack;
    }

    private bool IsDisposed => Volatile.Read(ref _disposed);

    /// <inheritdoc />
    public override bool CanRead => !IsDisposed;

    /// <inheritdoc />
    public override bool CanWrite => !IsDisposed;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Read is not supported in synchronous mode. Use ReadAsync instead.");
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // Cancel the read when either the caller cancels or the stream is disposed (_cts).
        // Dispose the linked CTS so it does not stay rooted on the parent tokens' registration lists.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);

        try
        {
            var reader = _connection.NetToAppReader;
            var readResult = await reader.ReadAsync(linkedCts.Token);

            if (readResult.IsCanceled || readResult is { IsCompleted: true, Buffer.IsEmpty: true })
                return 0;

            var bytesToCopy = (int)Math.Min(buffer.Length, readResult.Buffer.Length);
            readResult.Buffer.Slice(0, bytesToCopy).CopyTo(buffer.Span);
            reader.AdvanceTo(readResult.Buffer.GetPosition(bytesToCopy));
            _connection.OnAppConsumed(bytesToCopy); // DIAGNOSTIC: pipe drained

            return bytesToCopy;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return 0;
        }
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Write is not supported in synchronous mode. Use WriteAsync instead.");
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // Cancel the write when either the caller cancels or the stream is disposed (see ReadAsync).
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        try
        {
            await _connection.SendAppDataAsync(buffer, linkedCts.Token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Stream disposed
        }
    }

    /// <inheritdoc />
    public override void Flush() { }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        if (disposing) {
            _cts.TryCancel();
            _connection.TryStartFin(_stack);
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // Graceful close: drain buffered app->net bytes into TCP segments before FIN.
        await VhUtils.TryInvokeAsync("GracefulCloseAsync", 
            () => _connection.GracefulCloseAsync(_stack)).ConfigureAwait(false);
        
        await _cts.TryCancelAsync();
        _cts.Dispose();

        base.Dispose(false);
    }
}

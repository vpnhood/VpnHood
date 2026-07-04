using System.Buffers;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.TcpStack;

/// <summary>
/// A standard .NET Stream implementation for TCP connections through the local TCP stack.
/// Provides async read/write operations over TCP connections using System.IO.Pipelines for efficiency.
/// </summary>
public sealed class LocalTcpStream : AsyncStream
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
    /// <remarks>Stays true while the stream itself is alive: buffered data (up to and including the
    /// EOF after the peer's FIN) remains readable even after the connection closes.</remarks>
    public override bool CanRead => !IsDisposed;

    /// <inheritdoc />
    /// <remarks>Reflects CONNECTION liveness, not just stream disposal, so consumer health checks
    /// (e.g. ProxyChannel.CheckAlive via <c>Stream is { CanRead: true, CanWrite: true }</c>) observe a
    /// dead write path after a FIN/RST/idle-abort instead of pumping into a black hole.</remarks>
    public override bool CanWrite => !IsDisposed && !_connection.IsWriteClosed;

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // Link only when the caller's token is cancelable — saves a CTS allocation per read on the
        // proxy copy loop's hot path. Dispose the linked CTS so it does not stay rooted on the parent
        // tokens' registration lists.
        CancellationTokenSource? linkedCts = null;
        try
        {
            var token = _cts.Token;
            if (cancellationToken.CanBeCanceled) {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
                token = linkedCts.Token;
            }

            var reader = _connection.NetToAppReader;
            var readResult = await reader.ReadAsync(token);

            if (readResult.IsCanceled || readResult is { IsCompleted: true, Buffer.IsEmpty: true })
                return 0;

            var bytesToCopy = (int)Math.Min(buffer.Length, readResult.Buffer.Length);
            readResult.Buffer.Slice(0, bytesToCopy).CopyTo(buffer.Span);
            reader.AdvanceTo(readResult.Buffer.GetPosition(bytesToCopy));
            // FLOW CONTROL (not a diagnostic): draining is what reopens the advertised receive window
            // and triggers the window-update ACK that un-throttles the peer.
            _connection.OnAppConsumed(bytesToCopy);

            return bytesToCopy;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return 0;
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            // Dispose raced this read (its _cts was disposed under us): same outcome as a
            // post-dispose read — EOF.
            return 0;
        }
        finally
        {
            linkedCts?.Dispose();
        }
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // Cancel the write when either the caller cancels or the stream is disposed (see ReadAsync).
        CancellationTokenSource? linkedCts = null;
        try
        {
            var token = _cts.Token;
            if (cancellationToken.CanBeCanceled) {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
                token = linkedCts.Token;
            }

            await _connection.SendAppDataAsync(buffer, token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Stream disposed
        }
        finally
        {
            linkedCts?.Dispose();
        }
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
            () => _connection.GracefulCloseAsync(_stack)).Vhc();

        await _cts.TryCancelAsync();
        _cts.Dispose();

        base.Dispose(false);
    }
}

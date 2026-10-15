using System.Buffers;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Net.TcpStack;

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
        // proxy copy loop's hot path. The linked CTS is disposed with the read so it does not stay rooted
        // on the parent tokens' registration lists.
        try
        {
            using var linkedCts = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken)
                : null;
            var token = linkedCts?.Token ?? _cts.Token;
            var reader = _connection.NetToAppReader;
            var readResult = await reader.ReadAsync(token).Vhc();

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
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // Cancel the write when either the caller cancels or the stream is disposed (see ReadAsync).
        try
        {
            using var linkedCts = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken)
                : null;
            var token = linkedCts?.Token ?? _cts.Token;
            await _connection.SendAppDataAsync(buffer, token).Vhc();
        }
        catch (Exception ex) when ((ex is OperationCanceledException or IOException) && IsDisposed)
        {
            // disposed under the write, parked (the cancellation) or active (the closed connection): not every byte went
            // out, so it fails as a write after the dispose does
            throw new ObjectDisposedException("The stream was disposed while writing.", ex);
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
            _connection.CloseFromApp(_stack);
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _connection.CloseFromApp(_stack);
        await _cts.TryCancelAsync().Vhc();
        _cts.Dispose();

        base.Dispose(false);
    }
}

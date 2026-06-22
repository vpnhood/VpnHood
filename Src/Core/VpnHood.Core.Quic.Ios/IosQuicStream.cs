using Network;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Adapts a single QUIC stream (an <see cref="NWConnection"/> extracted from a multiplexed QUIC tunnel)
/// to a <see cref="Stream"/>, the surface VpnHood's tunneling code consumes.
/// </summary>
/// <remarks>
/// Network.framework send/receive are callback based; each call is bridged to a
/// <see cref="TaskCompletionSource"/>. Reads use the read-only-span receive overload so the native
/// buffer is copied synchronously inside the callback (the span must not escape it).
/// </remarks>
internal sealed class IosQuicStream(NWConnection connection) : Stream
{
    private int _disposed;

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());

        // minimumIncompleteLength: 1 -> return as soon as any data is available (stream semantics).
        connection.ReceiveReadOnlyData(minimumIncompleteLength: 1, maximumLength: (uint)count,
            (data, _, _, error) => {
                if (error != null) {
                    tcs.TrySetException(new IOException($"QUIC stream receive failed: {error}"));
                    return;
                }

                if (!data.IsEmpty)
                    data.CopyTo(buffer.AsSpan(offset, count));

                // A completed context with no further data is the stream FIN -> EOF (0 bytes).
                tcs.TrySetResult(data.Length);
            });

        return await tcs.Task.Vhc();
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());

        // isComplete: false -> more data may follow on this stream (do not signal FIN).
        connection.Send(buffer, offset, count, NWContentContext.DefaultMessage, isComplete: false,
            error => {
                if (error != null)
                    tcs.TrySetException(new IOException($"QUIC stream send failed: {error}"));
                else
                    tcs.TrySetResult();
            });

        await tcs.Task.Vhc();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && disposing) {
            try { connection.Cancel(); } catch { /* ignore */ }
            connection.Dispose();
        }
        base.Dispose(disposing);
    }
}

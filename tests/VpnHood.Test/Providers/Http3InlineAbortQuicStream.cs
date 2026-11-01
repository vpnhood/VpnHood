using System.Net.Quic;
using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Test.Providers;

internal sealed class Http3InlineAbortQuicStream(Stream output) : MemoryStream, IQuicStream
{
    // Native abort may run an awaiting parser inline, before AbortRead returns.
    private readonly TaskCompletionSource<int> _pendingRead = new();
    public long StreamId => 0;
    public Task ReadsClosed => _pendingRead.Task;
    public Task WritesClosed => Task.CompletedTask;
    public bool HasPendingRead { get; private set; }
    public bool IsAbortingRead { get; private set; }
    public int CompletedWrites { get; private set; }
    public long? ReadAbortCode { get; private set; }
    public long? WriteAbortCode { get; private set; }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HasPendingRead = true;
        return new ValueTask<int>(_pendingRead.Task);
    }

    public void CompleteWrites() => CompletedWrites++;

    public ValueTask WriteFinal(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var write = output.WriteAsync(buffer, cancellationToken);
        CompleteWrites();
        return write;
    }

    public void AbortRead(long errorCode)
    {
        ReadAbortCode = errorCode;
        IsAbortingRead = true;
        try {
            _pendingRead.TrySetException(new QuicException(QuicError.StreamAborted, errorCode,
                "The native read was aborted."));
        }
        finally {
            IsAbortingRead = false;
        }
    }

    public void AbortWrite(long errorCode) => WriteAbortCode = errorCode;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _pendingRead.TrySetException(new ObjectDisposedException(nameof(Http3InlineAbortQuicStream)));
        base.Dispose(disposing);
    }
}

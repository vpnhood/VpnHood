namespace VpnHood.Test.Providers;

internal sealed class Http3PendingReadStream : MemoryStream
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool CancellationObserved { get; private set; }
    public bool ReadCompleted { get; private set; }

    public Task WaitForRead(CancellationToken cancellationToken) => _started.Task.WaitAsync(cancellationToken);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _started.TrySetResult();
        try {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) {
            CancellationObserved = true;
            // Model an I/O completion that still touches its destination while cancellation wins.
            buffer.Span[0] = 0x7e;
            throw;
        }
        finally {
            ReadCompleted = true;
        }
    }
}

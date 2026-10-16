namespace VpnHood.Test.Providers;

// A stream that records which of its closes ran, and whose reads can fail. Empty, it reads as ended by its peer.
// Its async close can be held, as a QUIC stream's waits for its peer
public class CloseRecordingStream : MemoryStream
{
    private readonly TaskCompletionSource _asyncCloseRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _asyncCloseCount;
    public bool SyncClosed { get; private set; }
    public bool AsyncClosed => AsyncCloseCount > 0;
    public int AsyncCloseCount => Volatile.Read(ref _asyncCloseCount);
    public bool FailReads { get; init; }
    public bool HoldAsyncClose { get; init; }

    public void ReleaseAsyncClose() => _asyncCloseRelease.TrySetResult();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return FailReads
            ? ValueTask.FromException<int>(new IOException("A read failure for the test."))
            : base.ReadAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        SyncClosed = true;
        base.Dispose(disposing);
    }

    // the async close alone: the base's runs the sync one
    public override ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _asyncCloseCount);
        return HoldAsyncClose ? new ValueTask(_asyncCloseRelease.Task) : default;
    }
}

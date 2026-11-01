namespace VpnHood.Test.Providers;

internal sealed class Http3SynchronousReadHoldingStream(CancellationToken lifetimeToken) : MemoryStream
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _released = new();
    private int _resourcesDisposed;

    public Task WaitForRead(CancellationToken cancellationToken) => _started.Task.WaitAsync(cancellationToken);

    public void ReleaseRead() => _released.Set();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        _started.TrySetResult();
        _released.Wait(lifetimeToken);
        // Native adapters can observe cancellation before returning their asynchronous read handle.
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _resourcesDisposed, 1) == 0) {
            _released.Set();
            _released.Dispose();
        }
        base.Dispose(disposing);
    }
}

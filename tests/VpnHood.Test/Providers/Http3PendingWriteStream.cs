namespace VpnHood.Test.Providers;

internal sealed class Http3PendingWriteStream(CancellationToken lifetimeToken) : MemoryStream
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitForWrite(CancellationToken cancellationToken) => _started.Task.WaitAsync(cancellationToken);

    public Task WaitForCancellation(CancellationToken cancellationToken) => _canceled.Task.WaitAsync(cancellationToken);

    public Task WaitForWriteEnd(CancellationToken cancellationToken) => _ended.Task.WaitAsync(cancellationToken);

    public void ReleaseWrite() => _released.TrySetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await using var registration = cancellationToken.Register(OnCanceled);
        _started.TrySetResult();
        try {
            // Native cancellation may finish after its signal; ownership lasts until completion.
            await _released.Task.WaitAsync(lifetimeToken);
            cancellationToken.ThrowIfCancellationRequested();
            await base.WriteAsync(buffer, cancellationToken);
        }
        finally {
            _ended.TrySetResult();
        }
    }

    private void OnCanceled()
    {
        _canceled.TrySetResult();
    }
}

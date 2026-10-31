namespace VpnHood.Test.Providers;

internal sealed class Http3PendingWriteStream(CancellationToken lifetimeToken) : MemoryStream
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitForWrite(CancellationToken cancellationToken) => _started.Task.WaitAsync(cancellationToken);

    public Task WaitForCancellation(CancellationToken cancellationToken) => _canceled.Task.WaitAsync(cancellationToken);

    public void ReleaseWrite() => _released.TrySetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(OnCanceled);
        _started.TrySetResult();
        // Native cancellation may finish after its signal; the owner must keep the stream alive until then.
        await _released.Task.WaitAsync(lifetimeToken);
        cancellationToken.ThrowIfCancellationRequested();
        await base.WriteAsync(buffer, cancellationToken);
    }

    private void OnCanceled()
    {
        _canceled.TrySetResult();
    }
}

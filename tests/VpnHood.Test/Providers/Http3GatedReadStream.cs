namespace VpnHood.Test.Providers;

// Delivers one native read only after the test releases it. A second read stays pending until
// cancellation, so tests can distinguish an in-flight read from a new read after response submission.
internal sealed class Http3GatedReadStream : MemoryStream
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<byte[]> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _delivered;

    public Task WaitForRead(CancellationToken cancellationToken) => _started.Task.WaitAsync(cancellationToken);

    public void Release(byte[] bytes) => _released.TrySetResult(bytes);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_delivered) {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        _started.TrySetResult();
        var bytes = await _released.Task.WaitAsync(cancellationToken);
        if (bytes.Length > buffer.Length)
            throw new InvalidOperationException("The gated read exceeds its destination buffer.");
        bytes.AsMemory().CopyTo(buffer);
        _delivered = true;
        return bytes.Length;
    }
}

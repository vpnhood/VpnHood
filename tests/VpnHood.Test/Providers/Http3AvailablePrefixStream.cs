namespace VpnHood.Test.Providers;

internal sealed class Http3AvailablePrefixStream(IReadOnlyList<byte> prefix) : MemoryStream(prefix.ToArray())
{
    private readonly TaskCompletionSource<int> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int PendingReadsStarted { get; private set; }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Position < Length)
            return base.ReadAsync(buffer, cancellationToken);
        PendingReadsStarted++;
        return new ValueTask<int>(_pending.Task.WaitAsync(cancellationToken));
    }
}

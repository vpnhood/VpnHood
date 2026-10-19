namespace VpnHood.Test.Providers;

// A point where a test provider's calls wait while it is held, until released; it counts the calls that waited
public class TestHold
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _heldCount;
    public bool IsHeld { get; set; }
    public int HeldCount => Volatile.Read(ref _heldCount);

    public void Release() => _released.TrySetResult();

    // not cancellable: a held call ends on its release only, as one already past its cancellation would
    public async Task WaitIfHeld()
    {
        if (!IsHeld)
            return;

        Interlocked.Increment(ref _heldCount);
        await _released.Task;
    }

    // cancellable: a held call ends on its release or its cancellation, as a request on the network does
    public async Task WaitIfHeld(CancellationToken cancellationToken)
    {
        if (!IsHeld)
            return;

        Interlocked.Increment(ref _heldCount);
        await _released.Task.WaitAsync(cancellationToken);
    }
}

namespace VpnHood.Core.Server;

// Passes up to a number of acquisitions in each window of time, and refuses the rest. Lock-free: at a window's turn
// a race may misjudge a few, which a limit against floods can afford
internal sealed class WindowRateLimiter(int maxCount, TimeSpan window)
{
    private readonly long _windowMs = (long)window.TotalMilliseconds;
    private long _windowIndex;
    private int _count;

    public bool TryAcquire()
    {
        // the one caller that moves the window on starts its count; a late caller cannot move it back
        var windowIndex = Environment.TickCount64 / _windowMs;
        var lastWindowIndex = Volatile.Read(ref _windowIndex);
        if (windowIndex > lastWindowIndex &&
            Interlocked.CompareExchange(ref _windowIndex, windowIndex, lastWindowIndex) == lastWindowIndex)
            Volatile.Write(ref _count, 0);

        return Interlocked.Increment(ref _count) <= maxCount;
    }
}

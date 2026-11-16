namespace VpnHood.Core.Server.NetProtection;

// Passes up to a number of acquisitions in each window of time, and refuses the rest. Lock-free: the window and its
// count change together in one compare-exchange, so a window's turn can neither lose counts nor reset a later window
internal sealed class WindowRateLimiter(int maxCount, TimeSpan window)
{
    private readonly long _windowMs = (long)window.TotalMilliseconds;

    // the window's index in the high 32 bits, 136 years of uptime at one-second windows; its count in the low 32
    private long _state;

    public bool TryAcquire()
    {
        var windowIndex = Environment.TickCount64 / _windowMs;
        while (true) {
            // a new window starts at a count of zero; a late caller counts in the window that moved on
            var state = Volatile.Read(ref _state);
            var current = windowIndex > (state >>> 32) ? windowIndex << 32 : state;
            if ((int)current >= maxCount)
                return false; // a refusal writes nothing, so a flood over the limit contends for nothing

            if (Interlocked.CompareExchange(ref _state, current + 1, state) == state)
                return true;
        }
    }
}

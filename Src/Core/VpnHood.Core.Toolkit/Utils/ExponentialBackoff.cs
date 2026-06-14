namespace VpnHood.Core.Toolkit.Utils;

/// <summary>
/// Tracks consecutive failures and gates retries behind an exponential backoff window:
/// baseDelay, 2×baseDelay, 4×baseDelay ... capped at maxDelay. The window grows on each failure
/// and clears on success/reset. Not thread-safe; callers sharing an instance must synchronize.
/// </summary>
public class ExponentialBackoff(TimeSpan baseDelay, TimeSpan maxDelay)
{
    private DateTime _lastFailTime;

    /// <summary>Number of consecutive failures since the last reset.</summary>
    public int FailCount { get; private set; }

    /// <summary>The current backoff delay for the consecutive failure count (Zero when there are no failures).</summary>
    public TimeSpan CurrentDelay {
        get {
            if (FailCount == 0)
                return TimeSpan.Zero;
            var seconds = Math.Min(baseDelay.TotalSeconds * Math.Pow(2, FailCount - 1), maxDelay.TotalSeconds);
            return TimeSpan.FromSeconds(seconds);
        }
    }

    /// <summary>True when there is no pending failure or the backoff window has elapsed since the last failure.</summary>
    public bool IsReady => FailCount == 0 || FastDateTime.Now - _lastFailTime >= CurrentDelay;

    /// <summary>Records a failed attempt, growing the backoff window.</summary>
    public void OnFail()
    {
        FailCount++;
        _lastFailTime = FastDateTime.Now;
    }

    /// <summary>Clears the failure history so the next attempt is allowed immediately.</summary>
    public void Reset()
    {
        FailCount = 0;
    }
}

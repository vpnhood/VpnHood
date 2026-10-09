using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

internal sealed class TrafficMeterItem : IDisposable
{
    private const long NanosecondsPerMillisecond = 1_000_000;

    // A taker waits once its debt reaches this; a smaller debt carries to its next charge, so small charges
    // cost a timer every 20 ms at most, not one each
    private const long MinWaitNanoseconds = 20 * NanosecondsPerMillisecond;

    // The police lets bytes through while the debt stays within this: a taker that waits on the same bucket
    // keeps a debt of up to its threshold and one charge, and must not starve the police's packets
    private const long PoliceSlackNanoseconds = 100 * NanosecondsPerMillisecond;

    // A longer wait is cut here; the debt behind it carries to the next charge
    private const long MaxWaitNanoseconds = 24 * 60 * 60 * 1000 * NanosecondsPerMillisecond;

    private long _total;
    private long _lastTotal;
    private DateTime _lastSpeedUpdateTime = FastDateTime.UtcNow;
    private long _speed;
    private readonly Lock _speedLock = new();
    private bool _disposed;

    // The limit as virtual time: the moment by which all the bytes charged so far have passed at the limit,
    // on the monotonic tick clock in nanoseconds. Idle time moves it back no further than the burst, so a
    // pause saves at most the burst. The clock's 10-16 ms steps stay within the wait threshold
    private long _paidUntil = long.MinValue;

    public required TimeSpan SpeedInterval { get; init; }

    /// <remarks>Unit: bytes per second; 0 means no limit.</remarks>
    public long MaxSpeed { get; set; }

    /// <remarks>Unit: bytes; what may pass at full speed after a pause.</remarks>
    public long Burst { get; set; }

    public long Traffic => Interlocked.Read(ref _total);

    /// <remarks>Unit: bytes per second.</remarks>
    public long Speed {
        get {
            UpdateSpeed();
            lock (_speedLock)
                return _speed;
        }
    }

    public void OnTraffic(long bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Add(ref _total, bytes);
    }

    // charges the bytes, then waits while the debt is past the threshold
    public ValueTask ThrottleAsync(long bytes, CancellationToken cancellationToken)
    {
        if (MaxSpeed <= 0)
            return default;

        TryCharge(bytes, long.MaxValue, out var debt);
        return debt < MinWaitNanoseconds
            ? default
            : new ValueTask(Task.Delay(TimeSpan.FromTicks(Math.Min(debt, MaxWaitNanoseconds) / 100),
                cancellationToken));
    }

    // the police, for a path that cannot wait: charges the bytes, or refuses them uncharged
    public bool ShouldThrottle(long bytes)
    {
        return MaxSpeed > 0 && !TryCharge(bytes, PoliceSlackNanoseconds, out _);
    }

    private bool TryCharge(long bytes, long maxDebt, out long debt)
    {
        var cost = ToNanoseconds(bytes);
        var burstTime = ToNanoseconds(Burst);
        while (true) {
            var now = Environment.TickCount64 * NanosecondsPerMillisecond;
            var paidUntil = Volatile.Read(ref _paidUntil);
            var next = Math.Max(paidUntil, now - burstTime) + cost;
            debt = next - now;
            if (debt > maxDebt)
                return false;

            if (Interlocked.CompareExchange(ref _paidUntil, next, paidUntil) == paidUntil)
                return true;
        }
    }

    // the time the bytes take at the limit; a charge is never free
    private long ToNanoseconds(long bytes)
    {
        return Math.Max(1, (long)Math.Min(bytes * 1e9 / MaxSpeed, 1e17));
    }

    private void UpdateSpeed()
    {
        lock (_speedLock) {
            var now = FastDateTime.UtcNow;
            var duration = (now - _lastSpeedUpdateTime).TotalSeconds;
            if (duration < 1)
                return;

            var total = Interlocked.Read(ref _total);
            _speed = (long)((total - _lastTotal) / duration);
            _lastSpeedUpdateTime = now;
            _lastTotal = total;
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

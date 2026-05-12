using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

internal sealed class TrafficMeterItem : IDisposable
{
    private static readonly TimeSpan MaxThrottleDelay = TimeSpan.FromSeconds(2);
    private long _total;
    private long _lastTotal;
    private long _windowTotal;
    private DateTime _lastSpeedUpdateTime = FastDateTime.Now;
    private DateTime _windowStartTime = FastDateTime.Now;
    private long _speed;
    private readonly Lock _speedLock = new();
    private readonly SemaphoreSlim _throttleSemaphore = new(1, 1);
    private bool _disposed;

    public required TimeSpan SpeedInterval { get; init; }

    /// <remarks>Unit: bytes per second.</remarks>
    public long MaxSpeed { get; set; }
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
        Interlocked.Add(ref _windowTotal, bytes);
    }

    public bool ShouldThrottle(long bytes = 0)
    {
        return GetThrottleDelay(bytes) > TimeSpan.Zero;
    }

    public async ValueTask ThrottleAsync(CancellationToken cancellationToken)
    {
        var delay = GetThrottleDelay();
        if (delay <= TimeSpan.Zero)
            return;

        await _throttleSemaphore.WaitAsync(cancellationToken).Vhc();
        try {
            delay = GetThrottleDelay();
            if (delay <= TimeSpan.Zero)
                return;

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _windowTotal, 0);
            _windowStartTime = FastDateTime.Now;
        }
        finally {
            _throttleSemaphore.Release();
        }
    }

    private TimeSpan GetThrottleDelay(long bytes = 0)
    {
        if (_disposed || MaxSpeed is 0)
            return TimeSpan.Zero;

        var now = FastDateTime.Now;
        var elapsed = (now - _windowStartTime).TotalSeconds;
        if (elapsed < 0.1)
            elapsed = 0.1;

        var targetTime = (Interlocked.Read(ref _windowTotal) + bytes) / (double)MaxSpeed;
        var delaySeconds = targetTime - elapsed;
        return delaySeconds > 0
            ? TimeSpan.FromSeconds(Math.Min(delaySeconds, MaxThrottleDelay.TotalSeconds))
            : TimeSpan.Zero;
    }

    private void UpdateSpeed()
    {
        lock (_speedLock) {
            var now = FastDateTime.Now;
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
        if (_disposed)
            return;

        _disposed = true;
        _throttleSemaphore.Dispose();
    }
}
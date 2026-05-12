using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

public class TrafficMeter : IDisposable
{
    private long _totalSent;
    private long _totalReceived;
    private long _lastSent;
    private long _lastReceived;
    private long _windowSent;
    private long _windowReceived;
    private DateTime _lastSpeedUpdateTime = FastDateTime.Now;
    private DateTime _windowStartTime = FastDateTime.Now;
    private Traffic _speed;
    private readonly Lock _speedLock = new();
    private readonly SemaphoreSlim _throttleSemaphore = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// The interval used to calculate the current speed.
    /// </summary>
    public TimeSpan SpeedInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the total traffic transferred.
    /// </summary>
    public Traffic Traffic => new(Interlocked.Read(ref _totalSent), Interlocked.Read(ref _totalReceived));

    /// <summary>
    /// Gets the last activity time.
    /// </summary>
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;

    /// <summary>
    /// Gets or sets the maximum allowed speed (bytes per second) for throttling.
    /// Zero or negative means unlimited.
    /// </summary>
    public Traffic MaxSpeed { get; set; }

    /// <summary>
    /// Gets the current transfer speed.
    /// </summary>
    public Traffic Speed {
        get {
            UpdateSpeed();
            lock (_speedLock)
                return _speed;
        }
    }

    /// <summary>
    /// Reports sent bytes to the traffic meter. This method is thread-safe.
    /// </summary>
    public void OnSent(long bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Add(ref _totalSent, bytes);
        Interlocked.Add(ref _windowSent, bytes);
        LastActivityTime = FastDateTime.Now;
    }

    /// <summary>
    /// Reports received bytes to the traffic meter. This method is thread-safe.
    /// </summary>
    public void OnReceived(long bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Add(ref _totalReceived, bytes);
        Interlocked.Add(ref _windowReceived, bytes);
        LastActivityTime = FastDateTime.Now;
    }

    /// <summary>
    /// Waits if the current transfer rate exceeds the maximum allowed speed.
    /// Should be called by transfer producers before or after a transfer to enforce throttling.
    /// </summary>
    public async ValueTask ThrottleAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return;

        var maxSpeed = MaxSpeed;
        if (maxSpeed is { Sent: <= 0, Received: <= 0 })
            return;

        // Use semaphore to serialize throttle checks
        await _throttleSemaphore.WaitAsync(cancellationToken).Vhc();
        try {
            var now = FastDateTime.Now;
            var elapsed = (now - _windowStartTime).TotalSeconds;
            if (elapsed < 0.1)
                elapsed = 0.1; // avoid division by zero

            var currentSentRate = Interlocked.Read(ref _windowSent) / elapsed;
            var currentReceivedRate = Interlocked.Read(ref _windowReceived) / elapsed;

            // Calculate delay needed to bring rate within limits
            var delaySeconds = 0.0;

            if (maxSpeed.Sent > 0 && currentSentRate > maxSpeed.Sent) {
                var targetTime = Interlocked.Read(ref _windowSent) / (double)maxSpeed.Sent;
                delaySeconds = Math.Max(delaySeconds, targetTime - elapsed);
            }

            if (maxSpeed.Received > 0 && currentReceivedRate > maxSpeed.Received) {
                var targetTime = Interlocked.Read(ref _windowReceived) / (double)maxSpeed.Received;
                delaySeconds = Math.Max(delaySeconds, targetTime - elapsed);
            }

            if (delaySeconds > 0) {
                var delay = TimeSpan.FromSeconds(Math.Min(delaySeconds, 2)); // cap delay to 2 seconds
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            // Reset window periodically
            if (elapsed >= SpeedInterval.TotalSeconds) {
                Interlocked.Exchange(ref _windowSent, 0);
                Interlocked.Exchange(ref _windowReceived, 0);
                _windowStartTime = FastDateTime.Now;
            }
        }
        finally {
            _throttleSemaphore.Release();
        }
    }

    private void UpdateSpeed()
    {
        lock (_speedLock) {
            var now = FastDateTime.Now;
            var duration = (now - _lastSpeedUpdateTime).TotalSeconds;
            if (duration < 1)
                return;

            var totalSent = Interlocked.Read(ref _totalSent);
            var totalReceived = Interlocked.Read(ref _totalReceived);

            var sentSpeed = (long)((totalSent - _lastSent) / duration);
            var receivedSpeed = (long)((totalReceived - _lastReceived) / duration);
            _speed = new Traffic(sentSpeed, receivedSpeed);
            _lastSpeedUpdateTime = now;
            _lastSent = totalSent;
            _lastReceived = totalReceived;
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

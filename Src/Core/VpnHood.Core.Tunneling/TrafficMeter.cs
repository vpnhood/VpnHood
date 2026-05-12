using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

/// <summary>
/// Tracks tunnel traffic as two independent directions.
/// Throttling policy is intentionally applied by transport implementations, not by <see cref="Tunnel"/>:
/// async stream/proxy transports await to slow down naturally, while UDP transports drop packets when over limit.
/// For session-facing limits, server download maps to send and server upload maps to receive.
/// </summary>
public class TrafficMeter : IDisposable
{
    private readonly TrafficMeterItem _sent;
    private readonly TrafficMeterItem _received;
    private bool _disposed;

    /// <summary>
    /// The interval used to calculate the current speed.
    /// </summary>
    public TimeSpan SpeedInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TrafficMeter()
    {
        _sent = new TrafficMeterItem { SpeedInterval = SpeedInterval };
        _received = new TrafficMeterItem { SpeedInterval = SpeedInterval };
    }

    /// <summary>
    /// Gets the total traffic transferred.
    /// </summary>
    public Traffic Traffic => new(_sent.Traffic, _received.Traffic);

    /// <summary>
    /// Gets the last activity time.
    /// </summary>
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;

    /// <summary>
    /// Gets or sets the maximum allowed speed (bytes per second) for throttling.
    /// Null means unlimited.
    /// </summary>
    public Traffic? MaxSpeed {
        get => _sent.MaxSpeed is null && _received.MaxSpeed is null
            ? null
            : new Traffic(_sent.MaxSpeed ?? 0, _received.MaxSpeed ?? 0);
        set {
            _sent.MaxSpeed = value is { Sent: > 0 } ? value.Value.Sent : null;
            _received.MaxSpeed = value is { Received: > 0 } ? value.Value.Received : null;
        }
    }

    public long? SendMaxSpeed {
        get => _sent.MaxSpeed;
        set => _sent.MaxSpeed = value is > 0 ? value : null;
    }

    public long? ReceiveMaxSpeed {
        get => _received.MaxSpeed;
        set => _received.MaxSpeed = value is > 0 ? value : null;
    }

    /// <summary>
    /// Gets the current transfer speed.
    /// </summary>
    public Traffic Speed => new(_sent.Speed, _received.Speed);

    /// <summary>
    /// Reports sent bytes to the traffic meter. This method is thread-safe.
    /// </summary>
    public void OnSent(long bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _sent.OnTraffic(bytes);
        LastActivityTime = FastDateTime.Now;
    }

    /// <summary>
    /// Reports received bytes to the traffic meter. This method is thread-safe.
    /// </summary>
    public void OnReceived(long bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _received.OnTraffic(bytes);
        LastActivityTime = FastDateTime.Now;
    }

    public bool ShouldThrottleSend(long bytes = 0)
    {
        return _sent.ShouldThrottle(bytes);
    }

    public bool ShouldThrottleReceive(long bytes = 0)
    {
        return _received.ShouldThrottle(bytes);
    }

    public ValueTask ThrottleSendAsync(CancellationToken cancellationToken)
    {
        return _sent.ThrottleAsync(cancellationToken);
    }

    public ValueTask ThrottleSendAsync(long bytes, CancellationToken cancellationToken)
    {
        return _sent.ThrottleAsync(bytes, cancellationToken);
    }

    public ValueTask ThrottleReceiveAsync(CancellationToken cancellationToken)
    {
        return _received.ThrottleAsync(cancellationToken);
    }

    public ValueTask ThrottleReceiveAsync(long bytes, CancellationToken cancellationToken)
    {
        return _received.ThrottleAsync(bytes, cancellationToken);
    }

    public bool ShouldThrottle()
    {
        return ShouldThrottleSend() || ShouldThrottleReceive();
    }

    public async ValueTask ThrottleAsync(CancellationToken cancellationToken)
    {
        await ThrottleSendAsync(cancellationToken);
        await ThrottleReceiveAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sent.Dispose();
        _received.Dispose();
    }
}
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

/// <summary>
/// Tracks tunnel traffic as two independent directions, and holds the session's speed limit: one bucket per
/// direction, which every path of the session charges, so the limit covers all of its traffic. A sender
/// waits on it (the tunnel's queue for packets, each proxy stream for its reads); a receiver only enforces
/// it, slowing a stream or dropping a datagram the peer sent past the limit.
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
    public DateTime LastActivityTime { get; private set; } = FastDateTime.UtcNow;

    /// <summary>
    /// The maximum allowed speed (bytes per second) for throttling; 0 means unlimited.
    /// </summary>
    public Traffic MaxSpeed {
        get => new(_sent.MaxSpeed, _received.MaxSpeed);
        init {
            _sent.MaxSpeed = value.Sent;
            _received.MaxSpeed = value.Received;
        }
    }

    /// <summary>
    /// What may pass at full speed after a pause (bytes), before the limit holds.
    /// </summary>
    public Traffic MaxSpeedBurst {
        get => new(_sent.Burst, _received.Burst);
        init {
            _sent.Burst = value.Sent;
            _received.Burst = value.Received;
        }
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
        LastActivityTime = FastDateTime.UtcNow;
    }

    /// <summary>
    /// Reports received bytes to the traffic meter. This method is thread-safe.
    /// </summary>
    public void OnReceived(long bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _received.OnTraffic(bytes);
        LastActivityTime = FastDateTime.UtcNow;
    }

    /// <summary>
    /// Charges bytes to the send limit, and waits while the session is past it.
    /// </summary>
    public ValueTask ThrottleSendAsync(long bytes, CancellationToken cancellationToken)
    {
        return _sent.ThrottleAsync(bytes, cancellationToken);
    }

    /// <summary>
    /// Charges bytes to the receive limit, and waits while the session is past it.
    /// </summary>
    public ValueTask ThrottleReceiveAsync(long bytes, CancellationToken cancellationToken)
    {
        return _received.ThrottleAsync(bytes, cancellationToken);
    }

    /// <summary>
    /// For a receiver that cannot wait: charges bytes to the receive limit, or returns true, charging nothing,
    /// when they are past it and should be dropped.
    /// </summary>
    public bool ShouldThrottleReceive(long bytes)
    {
        return _received.ShouldThrottle(bytes);
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

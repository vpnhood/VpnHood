// ReSharper disable once CheckNamespace

namespace Ga4.Trackers.Ga4Measurements;

/// <summary>
/// Represents a tracker that sends Google Analytics 4's Measurement Protocol events.
/// </summary>
public interface IGa4MeasurementTracker : ITracker
{
    /// <summary>Sends events.</summary>
    /// <param name="ga4Events">The events.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task Track(IEnumerable<Ga4MeasurementEvent> ga4Events, CancellationToken cancellationToken);
}
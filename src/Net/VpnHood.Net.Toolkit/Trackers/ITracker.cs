// ReSharper disable once CheckNamespace

namespace Ga4.Trackers;

/// <summary>
/// Represents an analytics tracker, which sends events such as Google Analytics 4's.
/// </summary>
public interface ITracker
{
    /// <summary>Gets or sets a value indicating whether the tracker sends events; off, it drops them.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Sends events.</summary>
    /// <param name="trackEvents">The events.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken);

    /// <summary>Sends an event.</summary>
    /// <param name="trackEvent">The event.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Track(TrackEvent trackEvent, CancellationToken cancellationToken);
}
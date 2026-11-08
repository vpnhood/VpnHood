// ReSharper disable once CheckNamespace

namespace Ga4.Trackers.Ga4Tags;

/// <summary>
/// Represents a tracker that sends events the way a web page's Google tag (gtag.js) does.
/// </summary>
public interface IGa4TagTracker : ITracker
{
    /// <summary>Sends an event.</summary>
    /// <param name="ga4Event">The event.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task Track(Ga4TagEvent ga4Event, CancellationToken cancellationToken);
}
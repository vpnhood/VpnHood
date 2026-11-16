using System.Text.Json;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Trackers;

/// <summary>
/// Provides a tracker that sends nothing: it logs each batch of events at Debug, as JSON, to <c>VhLogger</c>, whatever its
/// switch and logger.
/// </summary>
public class NullTracker : TrackerBase
{
    /// <inheritdoc/>
    /// <remarks>It logs the events, with their parameters as text, and sends nothing.</remarks>
    public override Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
    {
        // convert TrackEvent parameters to a dictionary with string values for serialization
        trackEvents = trackEvents.Select(x => new TrackEvent {
            EventName = x.EventName,
            Parameters = x.Parameters.ToDictionary(kvp => kvp.Key, object? (kvp) => kvp.Value?.ToString()!)
        });

        VhLogger.Instance.LogDebug("TrackEvent. {TrackEvent}",
            JsonSerializer.Serialize(trackEvents));

        return Task.CompletedTask;
    }
}
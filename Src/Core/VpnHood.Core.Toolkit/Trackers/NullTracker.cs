using System.Text.Json;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Trackers;

public class NullTracker : TrackerBase
{
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
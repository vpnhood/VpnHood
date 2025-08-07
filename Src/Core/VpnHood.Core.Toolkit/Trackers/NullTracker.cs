using System.Text.Json;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Trackers;

public class NullTracker : TrackerBase
{
    public override Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("TrackEvent. {TrackEvent}", 
            JsonSerializer.Serialize(trackEvents));

        return Task.CompletedTask;
    }
}
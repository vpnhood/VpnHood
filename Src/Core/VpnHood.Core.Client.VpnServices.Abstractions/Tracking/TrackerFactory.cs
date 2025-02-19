using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using VpnHood.Core.Common.Trackers;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

public class BuiltInTrackerFactory : ITrackerFactory
{
    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        if (string.IsNullOrEmpty(createParams.Ga4MeasurementId))
            throw new InvalidOperationException("AppGa4MeasurementId is required to create a built-in tracker.");

        var tracker = new Ga4TagTracker {
            MeasurementId = createParams.Ga4MeasurementId,
            SessionCount = 1,
            ClientId = createParams.ClientId,
            SessionId = Guid.NewGuid().ToString(),
            UserProperties = new Dictionary<string, object> { { "client_version", createParams.ClientVersion.ToString(3) } }
        };

        if (!string.IsNullOrEmpty(createParams.UserAgent))
            tracker.UserAgent = createParams.UserAgent;

        _ = tracker.Track(new TrackEvent { EventName = TrackEventNames.SessionStart });

        return tracker;
    }
}
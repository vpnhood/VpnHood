using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using VpnHood.Core.Common.Trackers;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

// The library's own tracker: GA4 over its tag endpoint, to the measurement id it is given.
public class Ga4TrackerFactory : ITrackerFactory
{
    // Null sends nothing: a build that names no id collects nothing by design (an iOS head, a fork's).
    public string? MeasurementId { get; init; }

    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        if (string.IsNullOrEmpty(MeasurementId))
            return NullTrackerFactory.CreateNullTracker(createParams);

        var ga4TagTracker = new Ga4TagTracker {
            MeasurementId = MeasurementId,
            SessionCount = 1,
            ClientId = createParams.ClientId,
            SessionId = Guid.NewGuid().ToString(),
            IsEnabled = createParams.IsEnabled, // before the session_start below, so a switched-off one sends none
            UserProperties = new Dictionary<string, object>
                { { "client_version", createParams.ClientVersion.ToString(3) } }
        };

        if (!string.IsNullOrEmpty(createParams.UserAgent))
            ga4TagTracker.UserAgent = createParams.UserAgent;

        // use ITracker extension methods
        ITracker tracker = ga4TagTracker;
        _ = tracker.TryTrack(new TrackEvent { EventName = TrackEventNames.SessionStart });

        return tracker;
    }
}

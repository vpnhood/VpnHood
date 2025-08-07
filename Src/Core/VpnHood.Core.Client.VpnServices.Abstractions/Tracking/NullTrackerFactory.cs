using Ga4.Trackers;
using VpnHood.Core.Toolkit.Trackers;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

public class NullTrackerFactory : ITrackerFactory
{
    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        var tagTracker = new NullTracker {
            MeasurementId = "NullTracker",
            ClientId = createParams.ClientId,
            SessionId = Guid.NewGuid().ToString(),
            UserProperties = new Dictionary<string, object> { { "client_version", createParams.ClientVersion.ToString(3) } }
        };

        return tagTracker;
    }
}
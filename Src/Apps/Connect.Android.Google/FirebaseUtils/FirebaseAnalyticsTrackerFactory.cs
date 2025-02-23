using Ga4.Trackers;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

namespace VpnHood.App.Client.Droid.Google.FirebaseUtils;

public class FirebaseAnalyticsTrackerFactory : ITrackerFactory
{
    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        return FirebaseAnalyticsTracker.IsInit
            ? FirebaseAnalyticsTracker.Instance :
            new FirebaseAnalyticsTracker();
    }
}
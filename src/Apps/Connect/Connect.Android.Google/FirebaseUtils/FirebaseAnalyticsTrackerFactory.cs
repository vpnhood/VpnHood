using Ga4.Trackers;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

namespace VpnHood.App.Connect.Android.Google.FirebaseUtils;

public class FirebaseAnalyticsTrackerFactory : ITrackerFactory
{
    // The process's one instance, set to the switch it is made with: in the VPN service's own process
    // (a Release build's) nothing else sets it.
    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        var tracker = FirebaseAnalyticsTracker.IsInit
            ? FirebaseAnalyticsTracker.Instance
            : new FirebaseAnalyticsTracker();
        tracker.IsEnabled = createParams.IsEnabled;
        return tracker;
    }
}

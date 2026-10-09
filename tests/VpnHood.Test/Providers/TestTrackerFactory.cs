using Ga4.Trackers;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

namespace VpnHood.Test.Providers;

public class TestTrackerFactory : ITrackerFactory
{
    public static TestTracker TestTracker { get; set; } = new();

    // the one tracker for the app and the VPN service alike, set to the switch it is made with
    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        TestTracker.TrackEvents.Clear();
        TestTracker.IsEnabled = createParams.IsEnabled;
        return TestTracker;
    }
}
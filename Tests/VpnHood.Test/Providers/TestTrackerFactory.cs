using Ga4.Trackers;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

namespace VpnHood.Test.Providers;

public class TestTrackerFactory : ITrackerFactory
{
    public static TestTracker TestTracker { get; set; } = new();
    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        TestTracker.TrackEvents.Clear();
        return TestTracker;
    }
}
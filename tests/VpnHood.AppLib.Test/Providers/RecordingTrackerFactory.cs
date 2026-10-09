using System.Collections.Concurrent;
using Ga4.Trackers;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Test.Providers;

namespace VpnHood.AppLib.Test.Providers;

// Remembers each tracker it made and what it was made with: the app's, and the VPN service's, which the
// service makes with a copy of this factory, settings included. Its trackers record what they would
// send, only while switched on, and send nothing.
public class RecordingTrackerFactory : ITrackerFactory
{
    public static ConcurrentQueue<(string? FactoryName, TrackerCreateParams CreateParams, TestTracker Tracker)> Made { get; } = new();

    // a setting, which the VPN service's copy must carry too
    public string? Name { get; init; }

    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        var tracker = new TestTracker { IsEnabled = createParams.IsEnabled };
        Made.Enqueue((Name, createParams, tracker));
        return tracker;
    }
}

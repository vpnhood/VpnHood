using System.Collections.Concurrent;
using Ga4.Trackers;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

namespace VpnHood.AppLib.Test.Providers;

// Remembers each tracker it made and what it was made with: the app's, and the VPN service's, which the
// service makes with a factory of its own by this type's name. The trackers it makes send nothing.
public class RecordingTrackerFactory : ITrackerFactory
{
    public static ConcurrentQueue<(TrackerCreateParams CreateParams, ITracker Tracker)> Made { get; } = new();

    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        var tracker = NullTrackerFactory.CreateNullTracker(createParams);
        Made.Enqueue((createParams, tracker));
        return tracker;
    }
}

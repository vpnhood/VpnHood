using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Trackers;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

public static class BuiltInTrackerFactoryExtensions
{
    public static ITracker TryCreateTracker(this ITrackerFactory trackerFactory, TrackerCreateParams createParams)
    {
        try {
            return trackerFactory.CreateTracker(createParams);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Failed to create a tracker. Returning a null tracker instead.");
            return NullTrackerFactory.CreateNullTracker(createParams);
        }
    }

    // One tracker per factory. A null tracker adds nothing, so with none left this is null, with one
    // it is that one, and with more a composite that hands each the switch and the events.
    public static ITracker? TryCreateTracker(this IReadOnlyList<ITrackerFactory> trackerFactories,
        TrackerCreateParams createParams)
    {
        var trackers = trackerFactories
            .Select(x => x.TryCreateTracker(createParams))
            .Where(x => x is not NullTracker)
            .ToArray();

        return trackers.Length switch {
            0 => null,
            1 => trackers[0],
            _ => new CompositeTracker(trackers, createParams.IsEnabled)
        };
    }
}
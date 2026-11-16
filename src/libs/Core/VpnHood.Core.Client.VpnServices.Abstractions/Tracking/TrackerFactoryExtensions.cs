using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Trackers;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

/// <summary>
/// Provides the making of trackers from their factories, where a factory's failure costs only its tracker.
/// </summary>
public static class TrackerFactoryExtensions
{
    /// <summary>Makes a tracker, or one that sends nothing where the factory fails, which it logs.</summary>
    /// <param name="trackerFactory">The factory.</param>
    /// <param name="createParams">The client's ID, version and user agent, and the person's usage-data switch.</param>
    /// <returns>The tracker, or one that sends nothing.</returns>
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

    /// <summary>Makes a tracker of each factory, and joins those that send anything.</summary>
    /// <param name="trackerFactories">The factories.</param>
    /// <param name="createParams">The client's ID, version and user agent, and the person's usage-data switch.</param>
    /// <returns>
    /// <see langword="null"/> where none sends anything; that one where one does; otherwise a composite that hands each
    /// the switch and the events.
    /// </returns>
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
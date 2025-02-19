using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

public static class BuiltInTrackerFactoryExtensions
{
    public static ITracker? TryCreateTracker(this ITrackerFactory trackerFactory, TrackerCreateParams createParams)
    {
        try {
            return trackerFactory.CreateTracker(createParams);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Failed to create tracker.");
            return null;
        }
    }
}
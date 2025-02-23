using Ga4.Trackers;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

public interface ITrackerFactory
{
    ITracker CreateTracker(TrackerCreateParams createParams);
}
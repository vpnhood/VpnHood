using Ga4.Trackers;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

// Makes one tracker. The VPN service makes the factories again in its own process, from each one's type
// and public properties (TrackerFactorySerializer), so a factory keeps its settings - an id, a key - in
// public properties, has a public constructor without arguments, and makes a NullTracker where its
// SDK must not run.
public interface ITrackerFactory
{
    ITracker CreateTracker(TrackerCreateParams createParams);
}
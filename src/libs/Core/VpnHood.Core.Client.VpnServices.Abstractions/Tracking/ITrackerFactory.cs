using Ga4.Trackers;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

/// <summary>
/// Defines a maker of trackers.
/// </summary>
/// <remarks>
/// The VPN service makes the factories again in its own process, from each one's type and public properties
/// (<see cref="TrackerFactorySerializer"/>), so a factory keeps its settings — an ID, a key — in public properties, has
/// a public constructor without parameters, and makes a <c>NullTracker</c> where its SDK must not run.
/// </remarks>
public interface ITrackerFactory
{
    /// <summary>Makes a tracker.</summary>
    /// <param name="createParams">The client's ID, version and user agent, and the person's usage-data switch.</param>
    /// <returns>The tracker; one that sends nothing where its SDK must not run.</returns>
    ITracker CreateTracker(TrackerCreateParams createParams);
}
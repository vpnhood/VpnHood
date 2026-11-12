using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using VpnHood.Core.Common.Trackers;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

/// <summary>
/// Represents the library's own tracker factory: GA4 over its tag endpoint, to the measurement ID it is given.
/// </summary>
public class Ga4TrackerFactory : ITrackerFactory
{
    /// <summary>
    /// Gets the GA4 measurement ID, or <see langword="null"/>, which sends nothing: a build that names no ID collects
    /// nothing by design, as an iOS head or a fork's.
    /// </summary>
    public string? MeasurementId { get; init; }

    /// <inheritdoc/>
    /// <remarks>
    /// A tracker made switched on sends a session start at once. With no measurement ID, it is one that sends nothing.
    /// </remarks>
    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        if (string.IsNullOrEmpty(MeasurementId))
            return NullTrackerFactory.CreateNullTracker(createParams);

        var ga4TagTracker = new Ga4TagTracker {
            MeasurementId = MeasurementId,
            SessionCount = 1,
            ClientId = createParams.ClientId,
            SessionId = Guid.NewGuid().ToString(),
            IsEnabled = createParams.IsEnabled, // before the session_start below, so a switched-off one sends none
            UserProperties = new Dictionary<string, object>
                { { "client_version", createParams.ClientVersion.ToString(3) } }
        };

        if (!string.IsNullOrEmpty(createParams.UserAgent))
            ga4TagTracker.UserAgent = createParams.UserAgent;

        // use ITracker extension methods
        ITracker tracker = ga4TagTracker;
        _ = tracker.TryTrack(new TrackEvent { EventName = TrackEventNames.SessionStart }, CancellationToken.None);

        return tracker;
    }
}

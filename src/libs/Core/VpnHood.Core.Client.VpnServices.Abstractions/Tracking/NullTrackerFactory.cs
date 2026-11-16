using Ga4.Trackers;
using VpnHood.Net.Toolkit.Trackers;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

/// <summary>
/// Represents a tracker factory whose trackers send nothing.
/// </summary>
public class NullTrackerFactory : ITrackerFactory
{
    /// <summary>Makes a tracker that sends nothing, with the client's ID and version and the switch it is given.</summary>
    /// <param name="createParams">The client's ID, version and user agent, and the person's usage-data switch.</param>
    /// <returns>The tracker.</returns>
    public static ITracker CreateNullTracker(TrackerCreateParams createParams)
    {
        var tagTracker = new NullTracker {
            MeasurementId = "NullTracker",
            ClientId = createParams.ClientId,
            SessionId = Guid.NewGuid().ToString(),
            IsEnabled = createParams.IsEnabled,
            UserProperties = new Dictionary<string, object>
                { { "client_version", createParams.ClientVersion.ToString(3) } }
        };

        return tagTracker;
    }

    /// <inheritdoc/>
    public ITracker CreateTracker(TrackerCreateParams createParams)
    {
        return CreateNullTracker(createParams);
    }
}
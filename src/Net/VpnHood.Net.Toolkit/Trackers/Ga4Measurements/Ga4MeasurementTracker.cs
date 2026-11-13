// ReSharper disable once CheckNamespace

namespace Ga4.Trackers.Ga4Measurements;

/// <summary>
/// Provides a tracker that sends events through Google Analytics 4's Measurement Protocol, which a server or an app
/// uses with an API secret.
/// </summary>
/// <remarks>
/// Each event gets the session's ID, when there is one, and <c>debug_mode</c> with
/// <see cref="TrackerBase.IsAdminDebugView"/>, unless it has them already. A failed request throws only with
/// <see cref="TrackerBase.ThrowExceptionOnError"/>.
/// </remarks>
public class Ga4MeasurementTracker : TrackerBase, IGa4MeasurementTracker
{
    /// <summary>Gets the Measurement Protocol API secret of the data stream.</summary>
    public required string ApiSecret { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the events go to the validation endpoint, which checks them and records
    /// nothing.
    /// </summary>
    public bool IsDebugEndPoint { get; set; }

    /// <summary>Sends an event.</summary>
    /// <param name="ga4Event">The event.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">The event's parameter names collide when case is ignored, with the tracker on.</exception>
    /// <exception cref="Exception">The request failed, with <see cref="TrackerBase.ThrowExceptionOnError"/>.</exception>
    public Task Track(Ga4MeasurementEvent ga4Event, CancellationToken cancellationToken)
    {
        var tracks = new[] { ga4Event };
        return Track(tracks, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="ga4Events"/> is empty, with the tracker on, or an event's parameter names collide when case is ignored.</exception>
    /// <exception cref="Exception">The request failed, with <see cref="TrackerBase.ThrowExceptionOnError"/>.</exception>
    public Task Track(IEnumerable<Ga4MeasurementEvent> ga4Events, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return Task.CompletedTask;

        var gaEventArray = ga4Events.Select(x => (Ga4MeasurementEvent)x.Clone()).ToArray();
        if (!gaEventArray.Any())
            throw new ArgumentException("Events can not be empty! ", nameof(ga4Events));

        // updating events by default values
        foreach (var ga4Event in gaEventArray) {
            if (IsAdminDebugView && !ga4Event.Parameters.TryGetValue("debug_mode", out _))
                ga4Event.Parameters.Add("debug_mode", 1);

            if (!string.IsNullOrEmpty(SessionId) && !ga4Event.Parameters.TryGetValue("session_id", out _))
                ga4Event.Parameters.Add("session_id", SessionId);
        }


        var ga4Payload = new Ga4MeasurementPayload {
            ClientId = ClientId,
            UserId = UserId,
            Events = gaEventArray,
            UserProperties = UserProperties.Any()
                ? UserProperties.ToDictionary(p => p.Key,
                    p => new Ga4MeasurementPayload.UserProperty { Value = p.Value })
                : null
        };

        var baseUri = IsDebugEndPoint
            ? new Uri("https://www.google-analytics.com/debug/mp/collect")
            : new Uri("https://www.google-analytics.com/mp/collect");
        var requestMessage = new HttpRequestMessage(HttpMethod.Post,
            new Uri(baseUri,
                $"?api_secret={Uri.EscapeDataString(ApiSecret)}&measurement_id={Uri.EscapeDataString(MeasurementId)}"));
        PrepareHttpHeaders(requestMessage.Headers);
        return SendHttpRequest(requestMessage, "Measurement", ga4Payload, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="trackEvents"/> is empty, with the tracker on, or an event's parameter names collide when case is ignored.</exception>
    /// <exception cref="Exception">The request failed, with <see cref="TrackerBase.ThrowExceptionOnError"/>.</exception>
    public override Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
    {
        var ga4MeasurementEvents = trackEvents.Select(x =>
            new Ga4MeasurementEvent {
                EventName = x.EventName,
                Parameters = x.Parameters
            });

        return Track(ga4MeasurementEvents, cancellationToken);
    }
}
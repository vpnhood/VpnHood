// ReSharper disable once CheckNamespace
namespace Ga4.Trackers.Ga4Measurements;

public class Ga4MeasurementTracker : TrackerBase, IGa4MeasurementTracker
{
    public required string ApiSecret { get; init; }
    public bool IsDebugEndPoint { get; set; }

    public Task Track(Ga4MeasurementEvent ga4Event)
    {
        var tracks = new[] { ga4Event };
        return Track(tracks);
    }

    public Task Track(IEnumerable<Ga4MeasurementEvent> ga4Events)
    {
        if (!IsEnabled) 
            return Task.CompletedTask;

        var gaEventArray = ga4Events.Select(x => (Ga4MeasurementEvent)x.Clone()).ToArray();
        if (!gaEventArray.Any()) 
            throw new ArgumentException("Events can not be empty! ", nameof(ga4Events));

        // updating events by default values
        foreach (var ga4Event in gaEventArray)
        {
            if (IsAdminDebugView && !ga4Event.Parameters.TryGetValue("debug_mode", out _))
                ga4Event.Parameters.Add("debug_mode", 1);

            if (!string.IsNullOrEmpty(SessionId) && !ga4Event.Parameters.TryGetValue("session_id", out _))
                ga4Event.Parameters.Add("session_id", SessionId);
        }


        var ga4Payload = new Ga4MeasurementPayload
        {
            ClientId = ClientId,
            UserId = UserId,
            Events = gaEventArray,
            UserProperties = UserProperties.Any() ? UserProperties.ToDictionary(p => p.Key, p => new Ga4MeasurementPayload.UserProperty { Value = p.Value }) : null
        };

        var baseUri = IsDebugEndPoint ? new Uri("https://www.google-analytics.com/debug/mp/collect") : new Uri("https://www.google-analytics.com/mp/collect");
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"?api_secret={ApiSecret}&measurement_id={MeasurementId}"));
        PrepareHttpHeaders(requestMessage.Headers);
        return SendHttpRequest(requestMessage, "Measurement", ga4Payload);
    }

    public override Task Track(IEnumerable<TrackEvent> trackEvents)
    {
        var ga4MeasurementEvents = trackEvents.Select(x =>
            new Ga4MeasurementEvent
            {
                EventName = x.EventName,
                Parameters = x.Parameters
            });

        return Track(ga4MeasurementEvents);
    }
}
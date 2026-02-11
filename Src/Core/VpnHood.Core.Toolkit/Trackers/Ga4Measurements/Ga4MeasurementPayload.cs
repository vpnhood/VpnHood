using System.Text.Json.Serialization;

// ReSharper disable once CheckNamespace
namespace Ga4.Trackers.Ga4Measurements;

internal class Ga4MeasurementPayload
{
    public class UserProperty
    {
        [JsonPropertyName("value")]
        public required object Value { get; init; }
    }

    [JsonPropertyName("client_id")]
    public required string ClientId { get; set; }

    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; set; }

    [JsonPropertyName("timestamp_micros")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? TimestampMicros { get; set; }

    [JsonPropertyName("user_properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, UserProperty>? UserProperties { get; set; }

    [JsonPropertyName("events")]
    public required IEnumerable<Ga4MeasurementEvent> Events { get; set; }
}
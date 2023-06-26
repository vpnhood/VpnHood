using System.Collections.Generic;
using System.Text.Json.Serialization;

// ReSharper disable once CheckNamespace
namespace Ga4.Ga4Tracking;

internal class Ga4Payload
{
    public class UserProperty
    {
        [JsonPropertyName("value")]
        public required object Value { get; init; }
    }

    [JsonPropertyName("client_id")] 
    public required string ClientId { get; set; }

    [JsonPropertyName("timestamp_micros")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? TimestampMicros { get; set; }

    [JsonPropertyName("user_properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, UserProperty>? UserProperties { get; set; }
    
    [JsonPropertyName("events")]
    public required IEnumerable<Ga4Event> Events { get; set; }
}
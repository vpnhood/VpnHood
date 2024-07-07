using System.Text.Json.Serialization;

// ReSharper disable once CheckNamespace
namespace Ga4.Trackers.Ga4Measurements;

public class Ga4MeasurementEvent : ICloneable
{
    [JsonPropertyName("name")]
    public required string EventName { get; init; }

    [JsonPropertyName("params")]
    public Dictionary<string, object> Parameters { get; set; } = new();

    public object Clone()
    {
        var ret = new Ga4MeasurementEvent
        {
            EventName = EventName,
            Parameters = new Dictionary<string, object>(Parameters, StringComparer.OrdinalIgnoreCase)
        };

        return ret;
    }
}
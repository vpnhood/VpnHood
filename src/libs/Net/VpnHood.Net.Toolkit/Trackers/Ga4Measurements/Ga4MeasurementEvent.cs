using System.Text.Json.Serialization;

// ReSharper disable once CheckNamespace
namespace Ga4.Trackers.Ga4Measurements;

/// <summary>
/// Represents an event of Google Analytics 4's Measurement Protocol: its name and parameters.
/// </summary>
public class Ga4MeasurementEvent : ICloneable
{
    /// <summary>Gets the event's name.</summary>
    [JsonPropertyName("name")]
    public required string EventName { get; init; }

    /// <summary>Gets or sets the event's parameters, by name. The default is none.</summary>
    [JsonPropertyName("params")]
    public Dictionary<string, object?> Parameters { get; set; } = new();

    /// <summary>Copies the event, with a parameter dictionary of its own whose names ignore case.</summary>
    /// <returns>The copy.</returns>
    /// <exception cref="ArgumentException">Two parameter names collide when case is ignored.</exception>
    public object Clone()
    {
        var ret = new Ga4MeasurementEvent {
            EventName = EventName,
            Parameters = new Dictionary<string, object?>(Parameters, StringComparer.OrdinalIgnoreCase)
        };

        return ret;
    }
}
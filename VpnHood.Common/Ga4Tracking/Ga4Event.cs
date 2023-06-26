using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

// ReSharper disable once CheckNamespace
namespace Ga4.Ga4Tracking;

public class Ga4Event : ICloneable
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("params")]
    public Dictionary<string, object> Parameters { get; set; } = new();

    public object Clone()
    {
        var ret = new Ga4Event
        {
            Name = Name,
            Parameters = new Dictionary<string, object>(Parameters, StringComparer.OrdinalIgnoreCase)
        };

        return ret;
    }
}
#nullable enable
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Ga4.Ga4Tracking;

public class Ga4TagEvent
{
    public required string EventName { get; init; }
    public string? DocumentLocation { get; init; }
    public string? DocumentTitle { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();
}
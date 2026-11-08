// ReSharper disable once CheckNamespace

namespace Ga4.Trackers;

/// <summary>
/// Represents an analytics event: its name and parameters.
/// </summary>
public class TrackEvent
{
    /// <summary>Gets the event's name, such as <c>page_view</c>.</summary>
    public required string EventName { get; init; }

    /// <summary>Gets the event's parameters, by name. The default is none.</summary>
    public Dictionary<string, object?> Parameters { get; init; } = new();
}
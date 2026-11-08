// ReSharper disable once CheckNamespace

namespace Ga4.Trackers.Ga4Tags;

/// <summary>
/// Represents an event sent the way a web page's Google tag (gtag.js) sends it.
/// </summary>
public class Ga4TagEvent
{
    /// <summary>Gets the event's name.</summary>
    public required string EventName { get; init; }

    /// <summary>Gets the page's location, or <see langword="null"/>.</summary>
    public string? DocumentLocation { get; init; }

    /// <summary>Gets the page's title, or <see langword="null"/>.</summary>
    public string? DocumentTitle { get; init; }

    /// <summary>Gets or sets the page's referrer, or <see langword="null"/>.</summary>
    public string? DocumentReferrer { get; set; }

    /// <summary>Gets the event's properties, by name; an <see cref="int"/> or <see cref="long"/> is sent as a number.</summary>
    public Dictionary<string, object?> Properties { get; init; } = new();

    /// <summary>Gets a value indicating whether this is the user's first visit.</summary>
    public bool IsFirstVisit { get; init; }

    /// <summary>Gets the engagement time since the last event, in milliseconds, or <see langword="null"/>.</summary>
    public long? EngagementTime { get; init; }
}
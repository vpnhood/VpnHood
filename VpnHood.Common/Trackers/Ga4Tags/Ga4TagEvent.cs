// ReSharper disable once CheckNamespace
namespace Ga4.Trackers.Ga4Tags;

public class Ga4TagEvent
{
    public required string EventName { get; init; }
    public string? DocumentLocation { get; init; }
    public string? DocumentTitle { get; init; }
    public string? DocumentReferrer { get; set; }
    public Dictionary<string, object> Properties { get; init; } = new();
    public bool IsFirstVisit { get; init; }
    public long? EngagementTime { get; init; }
}
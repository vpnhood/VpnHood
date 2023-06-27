// ReSharper disable once CheckNamespace
namespace Ga4.Ga4Tracking;

public class Ga4TagParam
{
    public required string EventName { get; init; }
    public required string AppName { get; init; }
    public string DocumentLocation { get; init; } = "home";
    public string DocumentTitle { get; init; } = "Home Page";
}
// ReSharper disable once CheckNamespace
namespace Ga4.Trackers;

public interface ITracker
{
    public bool IsEnabled { get; set; }
    public Task Track(IEnumerable<TrackEvent> trackEvents, Dictionary<string, object>? userProperties = null);
    public Task Track(TrackEvent trackEvent, Dictionary<string, object>? userProperties = null) => Track([trackEvent], userProperties);
    public Task TrackError(string action, Exception ex);
}
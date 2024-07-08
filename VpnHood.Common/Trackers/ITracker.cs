// ReSharper disable once CheckNamespace
namespace Ga4.Trackers;

public interface ITracker
{
    bool IsEnabled { get; set; }
    Task Track(IEnumerable<TrackEvent> trackEvents);
    Task Track(TrackEvent trackEvent);
}
// ReSharper disable once CheckNamespace

namespace Ga4.Trackers;

public interface ITracker
{
    bool IsEnabled { get; set; }
    Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken);
    Task Track(TrackEvent trackEvent, CancellationToken cancellationToken);
}
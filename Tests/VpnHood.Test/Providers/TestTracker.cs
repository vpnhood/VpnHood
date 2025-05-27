using Ga4.Trackers;

namespace VpnHood.Test.Providers;

public class TestTracker : ITracker
{
    public bool IsEnabled { get; set; }
    public List<TrackEvent> TrackEvents { get; } = [];

    public Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
    {
        return Task.WhenAll(trackEvents.Select(x=>Track(x, cancellationToken)));
    }

    public Task Track(TrackEvent trackEvent, CancellationToken cancellationToken)
    {
        TrackEvents.Add(trackEvent);
        return Task.CompletedTask;
    }

    public TrackEvent? FindEvent(string eventName, string parameterName, object parameterValue)
    {
        return TrackEvents.FirstOrDefault(e =>
            e.EventName == eventName &&
            e.Parameters.ContainsKey(parameterName) && e.Parameters[parameterName].Equals(parameterValue));
    }
}
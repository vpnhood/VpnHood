using Ga4.Trackers;

namespace VpnHood.Test.Services;

internal class TestTracker : ITracker
{
    public bool IsEnabled { get; set; }
    public List<TrackEvent> TrackEvents { get; } = [];

    public Task Track(IEnumerable<TrackEvent> trackEvents)
    {
        return Task.WhenAll(trackEvents.Select(Track));
    }

    public Task Track(TrackEvent trackEvent)
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
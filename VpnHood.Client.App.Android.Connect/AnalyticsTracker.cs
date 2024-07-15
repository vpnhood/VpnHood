using Firebase.Analytics;
using Ga4.Trackers;

namespace VpnHood.Client.App.Droid.Connect;

public class AnalyticsTracker(FirebaseAnalytics analytics) : ITracker
{
    public bool IsEnabled { get; set; }
    public Task Track(IEnumerable<TrackEvent> trackEvents)
    {
        foreach (var trackEvent in trackEvents)
            TrackInternal(trackEvent);
        
        return Task.CompletedTask;
    }

    public Task Track(TrackEvent trackEvent)
    {
        TrackInternal(trackEvent);
        return Task.CompletedTask;
    }

    private void TrackInternal(TrackEvent trackEvent)
    {
        if (!IsEnabled)
            return;
        
        var bundle = new Bundle();
        foreach (var parameter in trackEvent.Parameters)
            bundle.PutString(parameter.Key, parameter.Value.ToString());
        analytics.LogEvent(trackEvent.EventName, bundle);
    }
}
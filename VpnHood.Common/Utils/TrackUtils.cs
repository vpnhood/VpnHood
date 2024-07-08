using Ga4.Trackers;

namespace VpnHood.Common.Utils;

public static class TrackUtils
{
    public static Task TrackError(ITracker tracker, string action, Exception ex)
    {
        var trackEvent = new TrackEvent
        {
            EventName = "exception",
            Parameters = new Dictionary<string, object>
            {
                { "page_location", "ex/" + action },
                { "page_title", ex.Message }
            }
        };

        return tracker.Track([trackEvent]);
    }
}
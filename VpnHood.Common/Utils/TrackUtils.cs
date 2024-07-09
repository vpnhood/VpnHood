using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Common.Utils;

public static class TrackUtils
{
    public static void TrackError(ITracker? tracker, Exception exception, string message, string action)
    {
        _ = TrackErrorAsync(tracker, exception, message, action);
    }

    public static async Task TrackErrorAsync(ITracker? tracker, Exception exception, string message, string action)
    {
        if (tracker == null)
            return;

        try
        {
            var trackEvent = new TrackEvent
            {
                EventName = "exception",
                Parameters = new Dictionary<string, object>
                {
                    { "method", action },
                    { "message", message },
                    { "error_message", exception.Message },
                }
            };

            await tracker.Track([trackEvent]);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not error to anonymous tracker.");
        }
    }
}
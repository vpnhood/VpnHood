using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Utils;

public static class TrackerExtensions
{
    public static async Task<bool> TryTrack(this ITracker tracker, TrackEvent trackEvent)
    {
        try {
            await tracker.Track(trackEvent).VhConfigureAwait();
            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Failed to track event.");
            return false;
        }
    }

    public static async Task<bool> TryTrack(this ITracker tracker, IEnumerable<TrackEvent> trackEvents)
    {
        try {
            await tracker.Track(trackEvents).VhConfigureAwait();
            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Failed to track events.");
            return false;
        }
    }

    public static Task<bool> TryTrackError(this ITracker tracker, Exception exception, string message, string action)
    {
        return TryTrackError(tracker, exception, message, action, false);
    }

    public static Task<bool> TryTrackWarningAsync(this ITracker tracker, Exception exception, string message, string action)
    {
        return TryTrackError(tracker, exception, message, action, true);
    }


    private static Task<bool> TryTrackError(this ITracker tracker, Exception exception, string message,
        string action, bool isWarning)
    {
        var trackEvent = new TrackEvent {
            EventName = "vh_exception",
            Parameters = new Dictionary<string, object> {
                    { "method", action },
                    { "message", message + ", " + exception.Message },
                    { "error_type", exception.GetType().Name },
                    { "error_level", isWarning ? "warning" : "error" }
                }
        };

        return tracker.TryTrack([trackEvent]);
    }
}
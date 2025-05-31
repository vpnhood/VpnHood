using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Common.Utils;

public static class VhTrackerExtensions
{
    public static Task VhTrackErrorAsync(this ITracker tracker, Exception exception, string message, string action,
        CancellationToken cancellationToken)
    {
        return VhTrackErrorAsync(tracker, exception, message, action, false, cancellationToken);
    }

    public static Task VhTrackWarningAsync(this ITracker tracker, Exception exception, string message, string action, 
        CancellationToken cancellationToken)
    {
        return VhTrackErrorAsync(tracker, exception, message, action, true, cancellationToken);
    }


    private static async Task VhTrackErrorAsync(this ITracker tracker, Exception exception, string message,
        string action, bool isWarning, CancellationToken cancellationToken)
    {
        try {
            var trackEvent = new TrackEvent {
                EventName = "vh_exception",
                Parameters = new Dictionary<string, object> {
                    { "method", action },
                    { "message", message + ", " + exception.Message },
                    { "error_type", exception.GetType().Name },
                    { "error_level", isWarning ? "warning" : "error" }
                }
            };

            await tracker.Track([trackEvent], cancellationToken);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not error to anonymous tracker.");
        }
    }
}
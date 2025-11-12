using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Utils;

public static class TrackerExtensions
{
    extension(ITracker tracker)
    {
        public Task<bool> TryTrack(TrackEvent trackEvent)
        {
            return TryTrackWithCancellation(tracker, trackEvent, CancellationToken.None);
        }

        public async Task<bool> TryTrackWithCancellation(TrackEvent trackEvent, 
            CancellationToken cancellationToken)
        {
            try {
                await tracker.Track(trackEvent, cancellationToken).Vhc();
                return true;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(ex, "Failed to track event.");
                return false;
            }
        }

        public async Task<bool> TryTrack(IEnumerable<TrackEvent> trackEvents)
        {
            try {
                await tracker.Track(trackEvents).Vhc();
                return true;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(ex, "Failed to track events.");
                return false;
            }
        }

        public Task<bool> TryTrackError(Exception exception, string message, string action)
        {
            return TryTrackError(tracker, exception, message, action, false);
        }

        public Task<bool> TryTrackWarningAsync(Exception exception, string message, string action)
        {
            return TryTrackError(tracker, exception, message, action, true);
        }

        private Task<bool> TryTrackError(Exception exception, string message,
            string action, bool isWarning)
        {
            var trackEvent = new TrackEvent {
                EventName = "vh_exception",
                Parameters = new Dictionary<string, object?> {
                    { "method", action },
                    { "message", message + ", " + exception.Message },
                    { "error_type", exception.GetType().Name },
                    { "error_level", isWarning ? "warning" : "error" }
                }
            };

            return tracker.TryTrack([trackEvent]);
        }
    }
}
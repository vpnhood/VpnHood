using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Extensions;

public static class TrackerExtensions
{
    extension(ITracker tracker)
    {
        public async Task<bool> TryTrack(TrackEvent trackEvent, CancellationToken cancellationToken)
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

        public async Task<bool> TryTrack(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
        {
            try {
                await tracker.Track(trackEvents, cancellationToken).Vhc();
                return true;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(ex, "Failed to track events.");
                return false;
            }
        }

        public Task<bool> TryTrackError(Exception exception, string? message, string action,
            CancellationToken cancellationToken, LogLevel logLevel = LogLevel.Error, TimeSpan? elapsed = null)
        {
            message = string.IsNullOrEmpty(message)
                ? exception.Message : message + ", " + exception.Message;

            var errorLevel = logLevel switch {
                >= LogLevel.Error => "error",
                LogLevel.Warning => "warning",
                _ => "info"
            };

            var trackEvent = new TrackEvent {
                EventName = "vh_exception",
                Parameters = new Dictionary<string, object?> {
                    { "method", action },
                    { "message", message },
                    { "error_type", exception.GetType().Name },
                    { "error_level", errorLevel }
                }
            };

            // whole seconds: the Firebase tracker sends values as text, and a fraction would follow the device's culture
            if (elapsed != null)
                trackEvent.Parameters.Add("elapsed_seconds", (int)elapsed.Value.TotalSeconds);

            return tracker.TryTrack([trackEvent], cancellationToken);
        }

        public Task<bool> TryTrackWarningAsync(Exception exception, string? message, string action,
            CancellationToken cancellationToken)
        {
            return tracker.TryTrackError(exception, message, action, cancellationToken, LogLevel.Warning);
        }
    }
}
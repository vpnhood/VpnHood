using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Extensions;

/// <summary>
/// Provides analytics tracking that logs a failure instead of throwing it.
/// </summary>
public static class TrackerExtensions
{
    /// <summary>
    /// Provides tracking that logs a failure instead of throwing it.
    /// </summary>
    /// <param name="tracker">The tracker.</param>
    extension(ITracker tracker)
    {
        /// <summary>Sends an event, and logs a failure at Debug instead of throwing it.</summary>
        /// <param name="trackEvent">The event.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task whose result is <see langword="true"/> if the event was sent; otherwise, <see langword="false"/>.</returns>
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

        /// <summary>Sends events, and logs a failure at Debug instead of throwing it.</summary>
        /// <param name="trackEvents">The events.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task whose result is <see langword="true"/> if the events were sent; otherwise, <see langword="false"/>.</returns>
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

        /// <summary>
        /// Sends an exception as a <c>vh_exception</c> event, with the action, the message, the exception's type and the
        /// level, and logs a failure instead of throwing it.
        /// </summary>
        /// <param name="exception">The exception, whose message is added to <paramref name="message"/>.</param>
        /// <param name="message">The message, or <see langword="null"/> for the exception's alone.</param>
        /// <param name="action">What was being done, sent as the event's method.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <param name="logLevel">The level, sent as error, warning or info. The default is Error.</param>
        /// <param name="elapsed">How long the action took, sent in whole seconds, or <see langword="null"/> for none.</param>
        /// <returns>A task whose result is <see langword="true"/> if the event was sent; otherwise, <see langword="false"/>.</returns>
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

        /// <summary>Sends an exception as a warning, as <see cref="TryTrackError"/> does.</summary>
        /// <param name="exception">The exception, whose message is added to <paramref name="message"/>.</param>
        /// <param name="message">The message, or <see langword="null"/> for the exception's alone.</param>
        /// <param name="action">What was being done, sent as the event's method.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task whose result is <see langword="true"/> if the event was sent; otherwise, <see langword="false"/>.</returns>
        public Task<bool> TryTrackWarningAsync(Exception exception, string? message, string action,
            CancellationToken cancellationToken)
        {
            return tracker.TryTrackError(exception, message, action, cancellationToken, LogLevel.Warning);
        }
    }
}
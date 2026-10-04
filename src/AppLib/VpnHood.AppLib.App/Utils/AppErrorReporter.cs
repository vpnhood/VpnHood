using System.Runtime.CompilerServices;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Utils;

// reports the app's failures to the log and to analytics
internal sealed class AppErrorReporter(ITracker tracker)
{
    public void ReportError(Exception ex, string? message, [CallerMemberName] string action = "n/a")
    {
        Report(ex, message, action, LogLevel.Error, elapsed: null);
    }

    // a cancel is the person's own doing, no error: a quick one is a change of mind, a late one means the
    // connect seemed stuck to them
    public void ReportConnectError(Exception ex, TimeSpan elapsed)
    {
        var logLevel = ex switch {
            UserCanceledException when elapsed < TimeSpan.FromSeconds(5) => LogLevel.Information,
            UserCanceledException => LogLevel.Warning,
            _ => LogLevel.Error
        };

        Report(ex, "Could not establish the connection.", nameof(VpnHoodApp.Connect), logLevel, elapsed);
    }

    private void Report(Exception ex, string? message, string action, LogLevel logLevel, TimeSpan? elapsed)
    {
        _ = tracker.TryTrackError(ex, message, action, logLevel, elapsed);
        VhLogger.Instance.Log(logLevel, ex, message);
    }
}

using System.Runtime.CompilerServices;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Utils;

// reports the app's failures to the log and to analytics
internal sealed class AppErrorReporter(ITracker tracker)
{
    public void ReportError(Exception ex, string? message, [CallerMemberName] string action = "n/a")
    {
        _ = tracker.TryTrackError(ex, message, action);
        VhLogger.Instance.LogError(ex, message);
    }
}

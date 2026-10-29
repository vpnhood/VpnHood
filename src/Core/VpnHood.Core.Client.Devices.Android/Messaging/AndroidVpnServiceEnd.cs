using System.Runtime.Versioning;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Android.Messaging;

// Why a VPN service the app took for running is gone. Android ends the service's process without a word to it - for
// an update just as for a force-stop, a crash or a restart of the device - so its status file still says connected.
// A session that ended shows as Disconnected (VpnServiceNotReadyException); a failure keeps its dialog
// (VpnServiceUnreachableException). Only Android 14 tells an update apart; before it an update is recorded as a
// force-stop, so there every end is a session that ended. A revoke never comes here: the service writes it before
// it stops.
internal static class AndroidVpnServiceEnd
{
    // An app still gets its own services from Android 8 on; before it, every app's, and another app built on VpnHood
    // has a service of the same class name. One waiting for its restart after a crash is listed too, and binds once
    // Android has started it again.
    public static bool IsListed()
    {
        var component = GetServiceComponent();
        return GetActivityManager().GetRunningServices(int.MaxValue)?.Any(x => component.Equals(x.Service)) == true;
    }

    public static Exception ToException()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(34))
            return Ended("not recorded apart before Android 14");

        var statusFile = Path.Combine(AndroidVpnService.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        var sinceTime = new DateTimeOffset(File.GetLastWriteTimeUtc(statusFile)).ToUnixTimeMilliseconds();
        var exitInfo = FindExitInfo(sinceTime);
        if (exitInfo == null) {
            // a restart of the device records no end
            var bootTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - SystemClock.ElapsedRealtime();
            return bootTime > sinceTime
                ? Ended("the device has restarted")
                : new VpnServiceUnreachableException("VpnService is unreachable. Android has no record of its end.");
        }

        var reason = (ApplicationExitInfoReason)exitInfo.Reason;
        return reason is ApplicationExitInfoReason.PackageUpdated or ApplicationExitInfoReason.UserRequested
            ? Ended(reason.ToString())
            : new VpnServiceUnreachableException($"VpnService is unreachable. Android has ended it. Reason: {reason}");
    }

    private static VpnServiceNotReadyException Ended(string reason)
    {
        VhLogger.Instance.LogInformation("Android has ended the VPN service, and the session with it. Reason: {Reason}",
            reason);
        return new VpnServiceNotReadyException($"Android has ended the VPN service. Reason: {reason}");
    }

    // The service's process's end since the service last wrote its status
    [SupportedOSPlatform("android34.0")]
    private static ApplicationExitInfo? FindExitInfo(long sinceTime)
    {
        var packageManager = Application.Context.PackageManager
            ?? throw new InvalidOperationException("Android gave no package manager.");
        var processName = packageManager.GetServiceInfo(GetServiceComponent(), PackageManager.ComponentInfoFlags.Of(0))
            .ProcessName;

        return GetActivityManager().GetHistoricalProcessExitReasons(packageName: null, pid: 0, maxNum: 0)
            .Where(x => x.ProcessName == processName && x.Timestamp >= sinceTime)
            .MaxBy(x => x.Timestamp);
    }

    private static ComponentName GetServiceComponent() =>
        new(Application.Context, Java.Lang.Class.FromType(typeof(AndroidVpnService)));

    private static ActivityManager GetActivityManager() =>
        (ActivityManager?)Application.Context.GetSystemService(Context.ActivityService)
        ?? throw new InvalidOperationException("Android gave no activity manager.");
}

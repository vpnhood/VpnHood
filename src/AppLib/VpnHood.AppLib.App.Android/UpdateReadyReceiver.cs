using Android.Content;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.Updaters;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Android;

// Tells the person that the update they started downloading is ready, when the app is not in front to
// show it in its notice. DownloadManager tells only a receiver in the manifest in time: since
// Android 14 one registered while the app runs waits until the app is in front again. Inert in a head
// whose app updates through its store: DownloadManager tells only the app that asked it to download.
[BroadcastReceiver(Exported = true, Permission = "android.permission.SEND_DOWNLOAD_COMPLETED_INTENTS")]
[IntentFilter([DownloadManager.ActionDownloadComplete])]
public class UpdateReadyReceiver : BroadcastReceiver
{
    // the VPN notification's channel: one per app, so this one needs no name of its own
    private const string ChannelId = "general";
    private const string ChannelName = "General";
    private const int NotificationId = 3501;

    public override void OnReceive(Context? context, Intent? intent)
    {
        try {
            if (context is null || !VpnHoodApp.IsInit || IsAppInFront())
                return;

            if (VpnHoodApp.Instance.Services.UpdaterService?.Status.UpdateState?.Stage != AppUpdateStage.Downloaded)
                return;

            Show(context, VpnHoodApp.Instance.Resources.Strings.UpdateReadyToInstall);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not tell the person that the update is ready.");
        }
    }

    private static bool IsAppInFront()
    {
        var processInfo = new ActivityManager.RunningAppProcessInfo();
        ActivityManager.GetMyMemoryState(processInfo);
        return processInfo.Importance == Importance.Foreground;
    }

    // A tap opens the app, whose notice shows Install: the install starts only on the person's press.
    private static void Show(Context context, string text)
    {
        var notificationManager = context.GetSystemService(Context.NotificationService) as NotificationManager ??
                                  throw new InvalidOperationException("The app has no NotificationManager.");
        var packageName = context.PackageName ??
                          throw new InvalidOperationException("The app has no package name.");
        var packageManager = context.PackageManager ??
                             throw new InvalidOperationException("The app has no PackageManager.");

        Notification.Builder builder;
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) {
            using var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Low);
            notificationManager.CreateNotificationChannel(channel);
            builder = new Notification.Builder(context, ChannelId);
        }
        else {
            builder = new Notification.Builder(context);
        }

        var launchIntent = packageManager.GetLaunchIntentForPackage(packageName);
        var pendingIntent = PendingIntent.GetActivity(context, NotificationId, launchIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var iconId = context.Resources?.GetIdentifier("notification", "drawable", packageName) ?? 0;
        if (iconId == 0)
            iconId = context.ApplicationInfo?.Icon ?? 0;

        using (builder) {
            builder.SetContentTitle(AndroidUtils.GetAppName(context));
            builder.SetContentText(text);
            builder.SetSmallIcon(iconId);
            builder.SetContentIntent(pendingIntent);
            builder.SetAutoCancel(true);
            notificationManager.Notify(NotificationId, builder.Build());
        }
    }
}

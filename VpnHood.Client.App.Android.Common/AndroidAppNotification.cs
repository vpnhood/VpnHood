using Android.Content;
using VpnHood.Client.App.Droid.Common.Utils;

namespace VpnHood.Client.App.Droid.Common;

public sealed class AndroidAppNotification : IDisposable
{
    public static int NotificationId => 1000;
    private const string NotificationChannelGeneralId = "general";
    private const string NotificationChannelGeneralName = "General";
    private readonly Notification.Builder _notificationBuilder;
    private readonly object _stateLock = new();
    private AppConnectionState _lastNotifyState = AppConnectionState.None;
    private readonly Context _context;
    private readonly VpnHoodApp _vpnHoodApp;

    public AndroidAppNotification(Context context, VpnHoodApp vpnHoodApp)
    {
        _context = context;
        _vpnHoodApp = vpnHoodApp;
        _notificationBuilder = CreateNotificationBuilder(context, vpnHoodApp.Resources);
    }

    public Notification Notification => _notificationBuilder.Build();

    private static PendingIntent CreatePendingIntent(Context context, string name)
    {
        var intent = new Intent(context, typeof(NotificationBroadcastReceiver));
        intent.SetAction(name);
        var pendingIntent = PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.Immutable)
            ?? throw new Exception("Could not acquire Broadcast intent.");

        return pendingIntent;
    }

    private static Notification.Builder CreateNotificationBuilder(Context context, AppResources appResources)
    {
        Notification.Builder notificationBuilder;

        // check notification manager
        var notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        ArgumentNullException.ThrowIfNull(notificationManager);
        ArgumentNullException.ThrowIfNull(context.PackageName);
        ArgumentNullException.ThrowIfNull(context.PackageManager);
        
        // open intent
        var openIntent = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);

        //create channel
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(NotificationChannelGeneralId, NotificationChannelGeneralName, NotificationImportance.Low);
            channel.EnableVibration(false);
            channel.EnableLights(false);
            channel.SetShowBadge(false);
            channel.LockscreenVisibility = NotificationVisibility.Public;
            notificationManager.CreateNotificationChannel(channel);
            notificationBuilder = new Notification.Builder(context, NotificationChannelGeneralId);
        }
        else
        {
            notificationBuilder = new Notification.Builder(context);
        }

        // for android 5.1 (no subtext will be shown if we don't call SetContentText)
        if (!OperatingSystem.IsAndroidVersionAtLeast(24))
            notificationBuilder.SetContentText(AndroidUtil.GetAppName(context));

        var pendingOpenIntent = PendingIntent.GetActivity(context, 0, openIntent, PendingIntentFlags.Immutable);
        notificationBuilder.SetContentIntent(pendingOpenIntent);
        notificationBuilder.AddAction(new Notification.Action.Builder(null, appResources.Strings.Disconnect, CreatePendingIntent(context, "disconnect")).Build());
        notificationBuilder.AddAction(new Notification.Action.Builder(null, appResources.Strings.Manage, pendingOpenIntent).Build());

        notificationBuilder.SetOngoing(true); // ignored by StartForeground
        notificationBuilder.SetAutoCancel(false); // ignored by StartForeground
        notificationBuilder.SetVisibility(NotificationVisibility.Secret); //VPN icon is already showed by the system
        if (appResources.Colors.WindowBackgroundColor != null)
            notificationBuilder.SetColor(appResources.Colors.WindowBackgroundColor.Value.ToAndroidColor());

        // set the required small icon
        ArgumentNullException.ThrowIfNull(context.ApplicationInfo);
        ArgumentNullException.ThrowIfNull(context.Resources);
        var iconId = context.Resources.GetIdentifier("@mipmap/notification", "drawable", context.PackageName);
        if (iconId == 0) iconId = context.ApplicationInfo.Icon;
        notificationBuilder.SetSmallIcon(iconId);

        return notificationBuilder;
    }

    public void Update(bool force = false)
    {
        lock (_stateLock)
        {
            // update only when the state changed
            var connectionState = _vpnHoodApp.ConnectionState;
            if (_lastNotifyState == connectionState && !force)
                return;

            // connection status
            // Set subtitle
            var activeProfileName = _vpnHoodApp.GetActiveClientProfile()?.Name;
            _notificationBuilder.SetContentTitle(activeProfileName);
            _notificationBuilder.SetSubText(connectionState == AppConnectionState.Connected
                ? $"{connectionState}"
                : $"{connectionState}...");

            // progress
            if (connectionState != AppConnectionState.Connected)
                _notificationBuilder.SetProgress(100, 0, true);
            else
                _notificationBuilder.SetProgress(0, 0, false);


            // show or hide
            var notificationManager = (NotificationManager?)_context.GetSystemService(Context.NotificationService);
            if (notificationManager == null)
                return;

            if (connectionState != AppConnectionState.None)
                notificationManager.Notify(NotificationId, _notificationBuilder.Build());
            else
                notificationManager.Cancel(NotificationId);

            // set it at the end of method to make sure change is applied without any exception
            _lastNotifyState = connectionState;
        }
    }

    public void Dispose()
    {
        _notificationBuilder.Dispose();
    }
}
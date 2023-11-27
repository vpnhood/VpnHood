using System;
using Android.App;
using Android.Content;
using Android.Graphics.Drawables;

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

    public AndroidAppNotification(Context context, Type mainActivity, VpnHoodApp vpnHoodApp)
    {
        _context = context;
        _vpnHoodApp = vpnHoodApp;
        _notificationBuilder = CreateNotificationBuilder(context, mainActivity, vpnHoodApp.Resources);
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

    private static Notification.Builder CreateNotificationBuilder(Context context, Type mainActivity, AppResources appResources)
    {
        Notification.Builder notificationBuilder;

        // check notification manager
        var notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService)
                                  ?? throw new Exception("Could not acquire NotificationManager.");

        // open intent
        var openIntent = new Intent(context, mainActivity);
        openIntent.AddFlags(ActivityFlags.NewTask);
        openIntent.SetAction(Intent.ActionMain);
        openIntent.AddCategory(Intent.CategoryLauncher);

        //create channel
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(NotificationChannelGeneralId, NotificationChannelGeneralName,
                NotificationImportance.Low);
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
            notificationBuilder.SetContentText(appResources.Strings.AppName);

        var pendingOpenIntent = PendingIntent.GetActivity(context, 0, openIntent, PendingIntentFlags.Immutable);
        notificationBuilder.SetContentIntent(pendingOpenIntent);
        notificationBuilder.AddAction(new Notification.Action.Builder(null, appResources.Strings.Disconnect, CreatePendingIntent(context, "disconnect")).Build());
        notificationBuilder.AddAction(new Notification.Action.Builder(null, appResources.Strings.Manage, pendingOpenIntent).Build());

        // Has problem with samsung android 6
        // todo check android 6
        // set the required small icon
        var icon = appResources.Icons.NotificationImage?.ToAndroidIcon();
        if (icon == null)
        {
            ArgumentNullException.ThrowIfNull(context.ApplicationInfo);   
            icon = Icon.CreateWithResource(context, context.ApplicationInfo.Icon);
        }
        notificationBuilder.SetSmallIcon(icon);

        if (appResources.Colors.WindowBackgroundColor != null)
            notificationBuilder.SetColor(appResources.Colors.WindowBackgroundColor.Value.ToAndroidColor());
        notificationBuilder.SetOngoing(true); // ignored by StartForeground
        notificationBuilder.SetAutoCancel(false); // ignored by StartForeground
        notificationBuilder.SetVisibility(NotificationVisibility.Secret); //VPN icon is already showed by the system

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
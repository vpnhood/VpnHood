using System;
using Android.App;
using Android.Content;
using VpnHood.Client.App.Resources;

namespace VpnHood.Client.App.Droid;

public sealed class AppNotification : IDisposable
{
    public int NotificationId => 1000;
    private const string NotificationChannelGeneralId = "general";
    private const string NotificationChannelGeneralName = "General";
    private readonly Context _context;
    private readonly Notification.Builder _notificationBuilder;
    private readonly object _stateLock = new();
    private AppConnectionState _lastNotifyState = AppConnectionState.None;
    public Notification Notification => _notificationBuilder.Build();

    public AppNotification(Context context)
    {
        _context = context;
        _notificationBuilder = CreateNotificationBuilder(context);
    }

    private static PendingIntent CreatePendingIntent(Context context, string name)
    {
        var intent = new Intent(context, typeof(NotificationBroadcastReceiver));
        intent.SetAction(name);
        var pendingIntent = PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.Immutable)
            ?? throw new Exception("Could not acquire Broadcast intent.");

        return pendingIntent;
    }

    private static Notification.Builder CreateNotificationBuilder(Context context)
    {
        Notification.Builder notificationBuilder;

        // check notification manager
        var notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService)
                                  ?? throw new Exception("Could not acquire NotificationManager.");

        // open intent
        var openIntent = new Intent(context, typeof(MainActivity));
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
            notificationBuilder.SetContentText(UiResource.AppName);

        var pendingOpenIntent = PendingIntent.GetActivity(context, 0, openIntent, PendingIntentFlags.Immutable);
        notificationBuilder.SetContentIntent(pendingOpenIntent);
        notificationBuilder.AddAction(new Notification.Action.Builder(null, UiResource.Disconnect, CreatePendingIntent(context, "disconnect")).Build());
        notificationBuilder.AddAction(new Notification.Action.Builder(null, UiResource.Manage, pendingOpenIntent).Build());

        // Has problem with samsung android 6
        // _notifyBuilder.SetSmallIcon(Android.Graphics.Drawables.Icon.CreateWithData(UiResource.NotificationImage, 0, UiResource.NotificationImage.Length));
        notificationBuilder.SetColor(App.BackgroundColor);
        notificationBuilder.SetSmallIcon(Resource.Mipmap.notification);
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
            var connectionState = VpnHoodApp.Instance.ConnectionState;
            if (_lastNotifyState == connectionState && !force)
                return;

            // connection status
            // Set subtitle
            var activeProfileName = VpnHoodApp.Instance.GetActiveClientProfile()?.Name;
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
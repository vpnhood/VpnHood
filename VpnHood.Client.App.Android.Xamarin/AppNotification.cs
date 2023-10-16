#nullable enable
using System;
using Android.App;
using Android.Content;
using VpnHood.Client.App.Resources;

namespace VpnHood.Client.App.Droid;

public sealed class AppNotification : IDisposable
{
    private const int NotificationId = 1000;
    private const string NotificationChannelGeneralId = "general";
    private const string NotificationChannelGeneralName = "General";
    private readonly Context _context;
    private readonly Notification.Builder _notifyBuilder;
    private readonly object _stateLock = new();
    private AppConnectionState _lastNotifyState = AppConnectionState.None;

    public AppNotification(Context context)
    {
        _context = context;
        
        // check notification manager
        var notificationManager = (NotificationManager?)_context.GetSystemService(Context.NotificationService) 
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
            channel.Importance = NotificationImportance.Low;
            notificationManager.CreateNotificationChannel(channel);
            _notifyBuilder = new Notification.Builder(_context, NotificationChannelGeneralId);
        }
        else
        {
#pragma warning disable CS0618
            _notifyBuilder = new Notification.Builder(_context);
#pragma warning restore CS0618
        }

        // for android 5.1 (no subtext will be shown if we don't call SetContentText)
        if (!OperatingSystem.IsAndroidVersionAtLeast(24))
            _notifyBuilder.SetContentText(UiResource.AppName);

        var pendingOpenIntent = PendingIntent.GetActivity(_context, 0, openIntent, PendingIntentFlags.Immutable);
        _notifyBuilder.SetContentIntent(pendingOpenIntent);
        _notifyBuilder.AddAction(new Notification.Action.Builder(null, UiResource.Disconnect, CreatePendingIntent("disconnect")).Build());
        _notifyBuilder.AddAction(new Notification.Action.Builder(null, UiResource.Manage, pendingOpenIntent).Build());

        // Has problem with samsung android 6
        // _notifyBuilder.SetSmallIcon(Android.Graphics.Drawables.Icon.CreateWithData(UiResource.NotificationImage, 0, UiResource.NotificationImage.Length));
        _notifyBuilder.SetSmallIcon(Resource.Mipmap.notification);
        _notifyBuilder.SetOngoing(true); // ignored by StartForeground
        _notifyBuilder.SetAutoCancel(false); // ignored by StartForeground
        _notifyBuilder.SetVisibility(NotificationVisibility.Secret); //VPN icon is already showed by the system
    }

    private PendingIntent CreatePendingIntent(string name)
    {
        var intent = new Intent(_context, typeof(NotificationBroadcastReceiver));
        intent.SetAction(name);
        var pendingIntent = PendingIntent.GetBroadcast(_context, 0, intent, PendingIntentFlags.Immutable) ?? throw new Exception("Could not acquire Broadcast intent.");
        return pendingIntent;
    }

    public void UpdateNotification()
    {
        lock (_stateLock)
        {
            // update only when the state changed
            var connectionState = VpnHoodApp.Instance.ConnectionState;
            if (_lastNotifyState == connectionState)
                return;

            // connection status
            _notifyBuilder.SetSubText(connectionState == AppConnectionState.Connected
                ? $"{connectionState}"
                : $"{connectionState}...");

            // progress
            if (connectionState != AppConnectionState.Connected)
                _notifyBuilder.SetProgress(100, 0, true);
            else
                _notifyBuilder.SetProgress(0, 0, false);

            // show or hide
            var notificationManager = (NotificationManager?)_context.GetSystemService(Context.NotificationService);
            if (notificationManager == null)
                return;

            if (connectionState != AppConnectionState.None)
                notificationManager.Notify(NotificationId, _notifyBuilder.Build());
            else
                notificationManager.Cancel(NotificationId);

            // set it at the end of method to make sure change is applied without any exception
            _lastNotifyState = connectionState;
        }
    }

    public void Dispose()
    {
        _notifyBuilder.Dispose();
    }
}
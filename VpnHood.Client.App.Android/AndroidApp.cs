#nullable enable
using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Android;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App.Android
{
#if DEBUG
    [Application(Debuggable = true, UsesCleartextTraffic = true)]
#else
    [Application(Debuggable = false, UsesCleartextTraffic = true)]
#endif
    internal class AndroidApp : Application, IAppProvider
    {
        private const int NotificationId = 1000;
        private const string NotificationChannelGeneralId = "general";
        private const string NotificationChannelGeneralName = "General";
        private Notification.Builder? _notifyBuilder;
        private AppConnectionState _lastNotifyState = AppConnectionState.None;

        public static AndroidApp? Current { get; private set; }
        public IDevice Device { get; }

        public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer)
        {
            Device = new AndroidDevice();
        }


        private void UpdateNotification()
        {
            if (_notifyBuilder == null)
                return; // _notifyBuilder has not been initialized yet

            lock (_notifyBuilder)
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
                var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
                if (notificationManager != null)
                {
                    if (connectionState != AppConnectionState.None)
                        notificationManager.Notify(NotificationId, _notifyBuilder.Build());
                    else
                        notificationManager.Cancel(NotificationId);
                }

                // set it at the end of method to make sure change is applied without any exception
                _lastNotifyState = connectionState;
            }
        }

        public override void OnCreate()
        {
            base.OnCreate();

            //app init
            VpnHoodApp.Init(this, new AppOptions());
            VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => UpdateNotification();
            InitNotification();
            Current = this;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (VpnHoodApp.IsInit)
                    VpnHoodApp.Instance.Dispose();
            }

            base.Dispose(disposing);
        }

        public PendingIntent CreatePendingIntent(string name)
        {
            var intent = new Intent(this, typeof(NotificationBroadcastReceiver));
            intent.SetAction(name);
            var pendingIntent = PendingIntent.GetBroadcast(this, 0, intent, 0) ?? throw new Exception("Could not acquire Broadcast intent!");
            return pendingIntent;
        }

        private void InitNotification()
        {
            // check notification manager
            var notificationManager = (NotificationManager?) GetSystemService(NotificationService);
            if (notificationManager == null)
            {
                VhLogger.Instance.LogError($"Could not acquire {nameof(NotificationManager)}!");
                return;
            }

            // open intent
            var openIntent = new Intent(this, typeof(MainActivity));
            openIntent.AddFlags(ActivityFlags.NewTask);
            openIntent.SetAction(Intent.ActionMain);
            openIntent.AddCategory(Intent.CategoryLauncher);
            var pendingOpenIntent = PendingIntent.GetActivity(this, 0, openIntent, 0);

            //create channel
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(NotificationChannelGeneralId, NotificationChannelGeneralName,
                    NotificationImportance.Low);
                channel.EnableVibration(false);
                channel.EnableLights(false);
                channel.SetShowBadge(false);
                channel.LockscreenVisibility = NotificationVisibility.Public;
                channel.Importance = NotificationImportance.Low;
                notificationManager.CreateNotificationChannel(channel);
                _notifyBuilder = new Notification.Builder(this, NotificationChannelGeneralId);
            }
            else
            {
#pragma warning disable CS0618 // Type or member is obsolete
                _notifyBuilder = new Notification.Builder(this);
#pragma warning restore CS0618 // Type or member is obsolete
            }

            _notifyBuilder.SetVisibility(NotificationVisibility.Secret); //VPN icon is already showed by the system
            _notifyBuilder.SetContentIntent(pendingOpenIntent);
            _notifyBuilder.AddAction(new Notification.Action(0, Resources?.GetText(Resource.String.disconnect) ?? "Disconnect", CreatePendingIntent("disconnect")));
            _notifyBuilder.AddAction(new Notification.Action(0, Resources?.GetText(Resource.String.manage) ?? "Manage", pendingOpenIntent));
            _notifyBuilder.SetSmallIcon(Resource.Mipmap.ic_notification);
            _notifyBuilder.SetOngoing(true); // ignored by StartForeground
            _notifyBuilder.SetAutoCancel(false); // ignored by StartForeground
        }
    }
}
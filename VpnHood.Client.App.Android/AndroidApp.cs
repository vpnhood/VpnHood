#nullable enable
using System;
using System.Timers;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Android;
using VpnHood.Logging;

namespace VpnHood.Client.App.Android
{

#if DEBUG
    [Application(Debuggable = true, UsesCleartextTraffic = true)]
#else
    [Application(Debuggable = false, UsesCleartextTraffic = true)]
#endif
    class AndroidApp : Application, IAppProvider
    {
        private const int NOTIFICATION_ID = 1000;
        private const string NOTIFICATION_CHANNEL_GENERAL_ID = "general";
        private const string NOTIFICATION_CHANNEL_GENERAL_NAME = "General";
        private readonly Timer _timer = new (1000);
        private Notification.Builder? _notifyBuilder;
        private AppConnectionState _lastNotifyState = AppConnectionState.None;

        public static AndroidApp? Current { get; private set; }
        private VpnHoodApp? VpnHoodApp { get; set; }
        public IDevice Device { get; }

        public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer)
        {
            Device = new AndroidDevice();
            _timer = new Timer(1000);
            _timer.Elapsed += Timer_Elapsed;
            _timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_notifyBuilder == null)
                return; // _notifyBuilder has not been initialized yet

            // update only when the state changed
            var connectionState = VpnHoodApp.Instance.State.ConnectionState;
            if (_lastNotifyState == connectionState)
                return;

            // connection status
            if (connectionState == AppConnectionState.Connected)
                _notifyBuilder.SetSubText($"{connectionState}");
            else
                _notifyBuilder.SetSubText($"{connectionState}...");

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
                    notificationManager.Notify(NOTIFICATION_ID, _notifyBuilder.Build());
                else
                    notificationManager.Cancel(NOTIFICATION_ID);
            }

            // set it at the end of method to make sure change is applied without any exception
            _lastNotifyState = connectionState;
        }

        public override void OnCreate()
        {
            base.OnCreate();

            //app init
            VpnHoodApp = VpnHoodApp.Init(this, new AppOptions { });
            InitNotifitication();
            Current = this;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                VpnHoodApp?.Dispose();
                VpnHoodApp = null;
            }

            base.Dispose(disposing);
        }

        public PendingIntent? CreatePendingIntent(string name)
        {
            var intent = new Intent(this, typeof(NotificationBroadcastReceiver));
            intent.SetAction(name);
            var pendingIntent = PendingIntent.GetBroadcast(this, 0, intent, 0) ?? throw new Exception("Could not acquire Broadcast intent!");
            return pendingIntent;
        }

        private void InitNotifitication()
        {
            // check notification manager
            var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
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
                var channel = new NotificationChannel(NOTIFICATION_CHANNEL_GENERAL_ID, NOTIFICATION_CHANNEL_GENERAL_NAME, NotificationImportance.Low);
                channel.EnableVibration(false);
                channel.EnableLights(false);
                channel.SetShowBadge(false);
                channel.LockscreenVisibility = NotificationVisibility.Public;
                channel.Importance = NotificationImportance.Low;
                notificationManager.CreateNotificationChannel(channel);
                _notifyBuilder = new Notification.Builder(this, NOTIFICATION_CHANNEL_GENERAL_ID);
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
            _notifyBuilder.SetOngoing(true); // ingored by StartForeground
            _notifyBuilder.SetAutoCancel(false); // ingored by StartForeground
        }
    }
}
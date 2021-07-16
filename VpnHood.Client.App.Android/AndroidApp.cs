using System;
using System.Timers;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Android;

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
        private readonly Timer _timer = new Timer(1000);
        private Notification.Builder _notifyBuilder;
        private NotificationManager NotificationManager => (NotificationManager)GetSystemService(NotificationService);
        private AppConnectionState _lastNotifyState = AppConnectionState.None;

        public static AndroidApp Current { get; private set; }
        private VpnHoodApp VpnHoodApp { get; set; }
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
            // update only when the state changed
            var state = VpnHoodApp.Current.State;
            if (_lastNotifyState == state.ConnectionState)
                return;
            _lastNotifyState = state.ConnectionState;

            // connection status
            if (state.ConnectionState == AppConnectionState.Connected)
                _notifyBuilder.SetSubText($"{state.ConnectionState}");
            else
                _notifyBuilder.SetSubText($"{state.ConnectionState}...");

            // progress
            if (state.ConnectionState != AppConnectionState.Connected)
                _notifyBuilder.SetProgress(100, 0, true);
            else
                _notifyBuilder.SetProgress(0, 0, false);

            // show or hide
            if (state.ConnectionState != AppConnectionState.None)
                NotificationManager.Notify(NOTIFICATION_ID, _notifyBuilder.Build());
            else
                NotificationManager.Cancel(NOTIFICATION_ID);

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

        public PendingIntent CreatePendingIntent(string name)
        {
            var intent = new Intent(this, typeof(NotificationBroadcastReceiver));
            intent.SetAction(name);
            var pendingIntent = PendingIntent.GetBroadcast(this, 0, intent, 0);
            return pendingIntent;
        }

        private void InitNotifitication()
        {
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
                NotificationManager.CreateNotificationChannel(channel);
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
            _notifyBuilder.AddAction(new Notification.Action(0, Resources.GetText(Resource.String.disconnect), CreatePendingIntent("disconnect")));
            _notifyBuilder.AddAction(new Notification.Action(0, Resources.GetText(Resource.String.manage), pendingOpenIntent));
            _notifyBuilder.SetSmallIcon(Resource.Mipmap.ic_notification);
            _notifyBuilder.SetOngoing(true); // ingored by StartForeground
            _notifyBuilder.SetAutoCancel(false); // ingored by StartForeground
        }
    }
}
#nullable enable
using System;
using Android.App;
using Android.Graphics;
using Android.Runtime;
using VpnHood.Client.App.Resources;

namespace VpnHood.Client.App.Droid
{
#if DEBUG
    [Application(Debuggable = true, Banner = "@mipmap/banner")]
#else
    [Application(Debuggable = false, Banner = "@mipmap/banner")]
#endif
    internal class App : Application
    {
        private AppNotification? _notification;
        public IAppProvider AppProvider { get; } = new AppProvider();
        public static App? Current { get; private set; }
        public static Color BackgroundColor => new (UiDefaults.WindowBackgroundColor.R, UiDefaults.WindowBackgroundColor.G, UiDefaults.WindowBackgroundColor.B, UiDefaults.WindowBackgroundColor.A);

        public App(IntPtr javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();
            Current = this;

            //app init
            if (!VpnHoodApp.IsInit) VpnHoodApp.Init(AppProvider);
            _notification = new AppNotification(this);
            VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => _notification.UpdateNotification();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (VpnHoodApp.IsInit) _ = VpnHoodApp.Instance.DisposeAsync();
                _notification?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
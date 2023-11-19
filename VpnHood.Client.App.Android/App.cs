using System;
using Android.App;
using Android.Graphics;
using Android.Runtime;
using VpnHood.Client.App.Resources;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid;
[Application(Banner = "@mipmap/banner", SupportsRtl = true, 
    UsesCleartextTraffic = true, // required to connect using self-signed certificates
    AllowBackup = true)]
internal class App : Application
{
    private AppNotification _appNotification = default!;

    public App(IntPtr javaReference, JniHandleOwnership transfer) 
        : base(javaReference, transfer)
    {
    }

    public IAppProvider AppProvider { get; private set; } = default!;
    public static App? Current { get; private set; }
    public static Color BackgroundColor => new (UiDefaults.WindowBackgroundColor.R, UiDefaults.WindowBackgroundColor.G, UiDefaults.WindowBackgroundColor.B, UiDefaults.WindowBackgroundColor.A);
    public static Color BackgroundBottomColor => new (UiDefaults.WindowBackgroundBottomColor.R, UiDefaults.WindowBackgroundBottomColor.G, UiDefaults.WindowBackgroundBottomColor.B, UiDefaults.WindowBackgroundBottomColor.A);
    public AndroidDevice VpnDevice => (AndroidDevice)AppProvider.Device;

    public override void OnCreate()
    {
        base.OnCreate();
        Current = this;

        //app init
        _appNotification = new AppNotification(this);
        AppProvider = new AppProvider { Device = new AndroidDevice(_appNotification.Notification, AppNotification.NotificationId) };
        if (!VpnHoodApp.IsInit) VpnHoodApp.Init(AppProvider);
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => _appNotification.Update();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (VpnHoodApp.IsInit) _ = VpnHoodApp.Instance.DisposeAsync();
            _appNotification.Dispose();
        }

        base.Dispose(disposing);
    }
}
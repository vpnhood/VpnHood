// ReSharper disable once RedundantNullableDirective
#nullable enable
using System;
using Android.App;
using Android.Graphics;
using Android.Runtime;
using VpnHood.Client.App.Resources;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid;

#if DEBUG
[Application(Debuggable = true, Banner = "@mipmap/banner")]
#else
    [Application(Debuggable = false, Banner = "@mipmap/banner")]
#endif
internal class App : Application
{
    private AppNotification _appNotification = default!;
    public IAppProvider AppProvider { get; private set; } = default!;
    public static App? Current { get; private set; }
    public static Color BackgroundColor => new (UiDefaults.WindowBackgroundColor.R, UiDefaults.WindowBackgroundColor.G, UiDefaults.WindowBackgroundColor.B, UiDefaults.WindowBackgroundColor.A);
    public AndroidDevice VpnDevice => (AndroidDevice)AppProvider.Device;

    public App(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        Current = this;

        //app init
        _appNotification = new AppNotification(this);
        AppProvider = new AppProvider { Device = new AndroidDevice(_appNotification.Notification, _appNotification.NotificationId) };
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
using System;
using Android.App;
using Android.Runtime;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid.Common;

[Application(Banner = "@mipmap/banner", Label = "@string/app_name", Icon = "@mipmap/appicon",
    UsesCleartextTraffic = true, // required for localhost
    SupportsRtl = true, AllowBackup = true)]

public class VpnHoodAndroidApp : Application
{
    private AppNotification _appNotification = default!;
    private static VpnHoodAndroidApp? _current;

    public VpnHoodAndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
        if (_current !=null)
            throw new InvalidOperationException($"{nameof(VpnHoodAndroidApp)} already created.");

        _current = this;
    }

    public IAppProvider AppProvider { get; private set; } = default!;
    public AndroidDevice VpnDevice { get; private set; } = default!;
    public static VpnHoodAndroidApp Current => _current ?? throw new InvalidOperationException($"{nameof(VpnHoodAndroidApp)} has not been initialized.");
    
    public override void OnCreate()
    {
        base.OnCreate();

        //app init
        VpnDevice = new AndroidDevice();
        AppProvider = new AppProvider { Device = VpnDevice };
        if (!VpnHoodApp.IsInit) VpnHoodApp.Init(AppProvider);

        // init notification
        _appNotification = new AppNotification(this, typeof(WebViewMainActivity), VpnHoodApp.Instance); //todo must get as parameter
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => _appNotification.Update();
        VpnDevice.InitNotification(_appNotification.Notification, AppNotification.NotificationId);
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
using System;
using Android.App;
using Android.Runtime;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid.Common;

public class AndroidApp : Application
{
    private AndroidAppNotification? _appNotification;

    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected virtual AppOptions AppOptions { get; } = new();

    public override void OnCreate()
    {
        base.OnCreate();

        //app init
        if (!VpnHoodApp.IsInit) 
            VpnHoodApp.Init(new AndroidAppProvider(), AppOptions);

        // init notification
        _appNotification = new AndroidAppNotification(this, typeof(AndroidAppWebViewMainActivity), VpnHoodApp.Instance); //todo WebViewMainActivity must get as parameter
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => _appNotification.Update();
        AndroidDevice.Current.InitNotification(_appNotification.Notification, AndroidAppNotification.NotificationId);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (VpnHoodApp.IsInit) _ = VpnHoodApp.Instance.DisposeAsync();
            _appNotification?.Dispose();
        }

        base.Dispose(disposing);
    }
}
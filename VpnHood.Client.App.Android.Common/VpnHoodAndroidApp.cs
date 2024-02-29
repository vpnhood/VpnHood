using Android.Runtime;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid.Common;

public abstract class VpnHoodAndroidApp(IntPtr javaReference, JniHandleOwnership transfer) 
    : Application(javaReference, transfer)
{
    private AndroidAppNotification? _appNotification;
    protected virtual AppOptions AppOptions { get; } = new();

    public override void OnCreate()
    {
        base.OnCreate();

        //app init
        if (!VpnHoodApp.IsInit) 
            VpnHoodApp.Init(new AndroidDevice(), AppOptions);

        // init notification
        _appNotification = new AndroidAppNotification(VpnHoodApp.Instance); 
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
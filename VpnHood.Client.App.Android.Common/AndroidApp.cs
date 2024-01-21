using Android.Runtime;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid.Common;

public class AndroidApp : Application
{
    private AndroidAppNotification? _appNotification;
    protected virtual AppOptions AppOptions { get; } = new();

    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();

        //app init
        if (!VpnHoodApp.IsInit) 
            VpnHoodApp.Init(new AndroidDevice(), AppOptions);

        // init notification
        _appNotification = new AndroidAppNotification(this, VpnHoodApp.Instance); 
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
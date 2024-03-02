using Android.App;
using Android.Runtime;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.Samples.MauiAppSpaSample;


[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership)
    : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp()
    {
        var vpnHoodDevice = new AndroidDevice();
        var mauiApp = MauiProgram.CreateMauiApp(vpnHoodDevice);
        var appNotification = new AndroidAppNotification(VpnHoodApp.Instance);
        vpnHoodDevice.InitNotification(appNotification.Notification, AndroidAppNotification.NotificationId);
        return mauiApp;
    }
}
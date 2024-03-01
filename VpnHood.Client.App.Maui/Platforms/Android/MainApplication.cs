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
    private AndroidAppNotification? _appNotification;

    protected override MauiApp CreateMauiApp()
    {
        var mauiApp =  MauiProgram.CreateMauiApp(new AndroidDevice());
        
        _appNotification = new AndroidAppNotification(VpnHoodApp.Instance);
        AndroidDevice.Current.InitNotification(_appNotification.Notification, AndroidAppNotification.NotificationId);

        return mauiApp;
    }
}
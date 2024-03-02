using Android.Runtime;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid.Common;

public abstract class VpnHoodAndroidApp(IntPtr javaReference, JniHandleOwnership transfer) 
    : Application(javaReference, transfer)
{
    protected virtual AppOptions AppOptions { get; } = new();

    public override void OnCreate()
    {
        base.OnCreate();

        //app init
        if (!VpnHoodApp.IsInit)
        {
            var vpnHoodDevice = new AndroidDevice();
            var vpnHoodApp = VpnHoodApp.Init(vpnHoodDevice, AppOptions);
            vpnHoodDevice.InitNotification(new AndroidAppNotification(vpnHoodApp).DeviceNotification);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (VpnHoodApp.IsInit) _ = VpnHoodApp.Instance.DisposeAsync();
        }

        base.Dispose(disposing);
    }
}
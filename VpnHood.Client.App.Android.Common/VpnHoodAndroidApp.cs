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
            var options = AppOptions;
            options.UiService ??= new AndroidAppUiService();

            var vpnHoodDevice = AndroidDevice.Create();
            var vpnHoodApp = VpnHoodApp.Init(vpnHoodDevice, options);
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
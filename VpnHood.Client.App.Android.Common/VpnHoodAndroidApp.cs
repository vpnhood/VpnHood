using Android.Runtime;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid.Common;

public abstract class VpnHoodAndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
    : Application(javaReference, transfer)
{
    protected abstract AppOptions CreateAppOptions();

    public override void OnCreate()
    {
        base.OnCreate();

        //app init
        if (!VpnHoodApp.IsInit) {
            var options = CreateAppOptions();
            options.UiService ??= new AndroidAppUiService();
            options.CultureService ??= AndroidAppAppCultureService.CreateIfSupported();

            var vpnHoodDevice = AndroidDevice.Create();
            var vpnHoodApp = VpnHoodApp.Init(vpnHoodDevice, options);
            vpnHoodDevice.InitNotification(new AndroidAppNotification(vpnHoodApp).DeviceNotification);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodApp.IsInit) _ = VpnHoodApp.Instance.DisposeAsync();
        }

        base.Dispose(disposing);
    }
}
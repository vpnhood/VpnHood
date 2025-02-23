using Android.Runtime;
using Ga4.Trackers;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Client.Device.Droid.Utils;

namespace VpnHood.AppLib.Droid.Common;

public abstract class VpnHoodAndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
    : Application(javaReference, transfer)
{
    protected abstract AppOptions CreateAppOptions();
    protected virtual Type GetVpnServiceType() => typeof(ITracker);

    public override void OnCreate()
    {
        base.OnCreate();

        //app init
        if (!VpnHoodApp.IsInit) {
            var options = CreateAppOptions();
            options.UiProvider ??= new AndroidUiProvider();
            options.CultureProvider ??= AndroidAppCultureProvider.CreateIfSupported();
            options.DeviceId ??= AndroidUtil.GetDeviceId(this); //this will be hashed using AppId

            var vpnHoodDevice = AndroidDevice.Create();
            VpnHoodApp.Init(vpnHoodDevice, options);
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
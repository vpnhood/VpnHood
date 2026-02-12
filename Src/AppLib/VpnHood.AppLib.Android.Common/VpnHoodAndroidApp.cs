using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Droid.Common;

public class VpnHoodAndroidApp : Singleton<VpnHoodAndroidApp>
{
    public static VpnHoodAndroidApp Init(Func<AppOptions> optionsFactory)
    {
        AndroidEnvironment.UnhandledExceptionRaiser += OnUnhandledExceptionRaiser;


        // do not init again or in the vpn service process
        if (VpnHoodApp.IsInit || AndroidDevice.IsVpnServiceProcess)
            return new VpnHoodAndroidApp();

        //app init
        var options = optionsFactory();
        options.DeviceUiProvider ??= new AndroidDeviceUiProvider();
        options.CultureProvider ??= AndroidAppCultureProvider.CreateIfSupported();
        options.DeviceId ??= AndroidUtil.GetDeviceId(Application.Context); //this will be hashed using AppId

        var vpnHoodDevice = AndroidDevice.Create();
        VpnHoodApp.Init(vpnHoodDevice, options);
        return new VpnHoodAndroidApp();
    }

    private static void OnUnhandledExceptionRaiser(object? sender, RaiseThrowableEventArgs args)
    {
        // Log the error to your analytics service here
        var exception = args.Exception;
        VhLogger.Instance.LogError(exception, "Unhandled exception in Android environment");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodApp.IsInit)
                VpnHoodApp.Instance.Dispose();
        }

        base.Dispose(disposing);
    }
}
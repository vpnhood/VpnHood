using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Android;

// The app on Android. The OS starts the head's Application in every process of the package - the
// VPN service's and the Quick Settings tile's too - and only the app's own process may build
// anything: so the init params come as a factory, called only there, and a head's configuration
// is never read where it is not needed.
public class VpnHoodAndroidApp : Singleton<VpnHoodAndroidApp>
{
    public static VpnHoodAndroidApp Init(Func<AppInitParams> initParamsFactory)
    {
        if (IsInit)
            return Instance;

        AndroidEnvironment.UnhandledExceptionRaiser += OnUnhandledExceptionRaiser;

        // do not init again, or in any process but the app's own: the VPN service's, the tile's, or any
        // other a library adds
        if (VpnHoodApp.IsInit || !AndroidDevice.IsMainProcess)
            return new VpnHoodAndroidApp();

        var initParams = initParamsFactory();
        var context = new AppOptionsContext {
            AppId = initParams.AppId,
            StoragePath = initParams.ResolveStoragePath(),
            PackagedAssetProvider = new AndroidAssetProvider(Application.Context)
        };

        var options = initParams.AppOptionsFactory(context);
        options.DeviceUiProvider ??= new AndroidDeviceUiProvider();
        options.CultureProvider ??= AndroidAppCultureProvider.CreateIfSupported();
        options.DeviceId ??= AndroidUtils.GetDeviceId(Application.Context); //this will be hashed using AppId

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
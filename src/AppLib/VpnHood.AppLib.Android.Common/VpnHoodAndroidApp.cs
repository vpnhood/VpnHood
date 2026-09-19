using Android.Runtime;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Assets;
using VpnHood.Core.Client.Devices.Droid;
using VpnHood.Core.Client.Devices.Droid.Utils;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Droid.Common;

public class VpnHoodAndroidApp : Singleton<VpnHoodAndroidApp>
{
    public static VpnHoodAndroidApp Init(Func<AppOptions> optionsFactory)
    {
        AndroidEnvironment.UnhandledExceptionRaiser += OnUnhandledExceptionRaiser;


        // do not init again or in the vpn service/tile processes
        if (VpnHoodApp.IsInit || AndroidDevice.IsVpnServiceProcess || QuickLaunchTileService.IsTileProcess)
            return new VpnHoodAndroidApp();

        //app init
        var options = optionsFactory();
        options.DeviceUiProvider ??= new AndroidDeviceUiProvider();
        options.CultureProvider ??= AndroidAppCultureProvider.CreateIfSupported();
        options.DeviceId ??= AndroidUtils.GetDeviceId(Application.Context); //this will be hashed using AppId

        var vpnHoodDevice = AndroidDevice.Create();
        VpnHoodApp.Init(vpnHoodDevice, options);

        // the UI's assets folder: a web server and a font collection are handed a FOLDER and read it
        // themselves, so this one is copied out of the package, on first use. Single FILES are not -
        // AndroidAssetProvider reads those where they lie, and each head builds its own.
        AppContent.FolderResolver = () => AndroidAppContent.Extract(Application.Context, VpnHoodApp.Instance.StorageFolderPath);
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
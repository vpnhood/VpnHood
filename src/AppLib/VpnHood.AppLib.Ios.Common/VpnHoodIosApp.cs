using VpnHood.Core.Client.Devices;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Ios.Common;

// iOS counterpart of VpnHoodAndroidApp. Bootstraps VpnHoodApp with iOS defaults.
// Unlike Android, the concrete IosDevice needs app-specific identifiers (App Group id and the
// Network-Extension bundle id), so the host app constructs the device and passes it in.
public class VpnHoodIosApp : Singleton<VpnHoodIosApp>
{
    public static VpnHoodIosApp Init(IDevice device, AppOptions options)
    {
        // do not init again
        if (VpnHoodApp.IsInit)
            return new VpnHoodIosApp();

        options.DeviceUiProvider ??= new IosDeviceUiProvider();
        options.CultureProvider ??= IosAppCultureProvider.CreateIfSupported();

        VpnHoodApp.Init(device, options);
        return new VpnHoodIosApp();
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

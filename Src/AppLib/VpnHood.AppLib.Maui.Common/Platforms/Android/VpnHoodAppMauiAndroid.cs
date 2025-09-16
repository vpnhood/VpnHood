using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Droid.Common;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppLib.Maui.Common;

internal class VpnHoodAppMauiAndroid : Singleton<VpnHoodAppMauiAndroid>, IVpnHoodAppMaui
{
    public static VpnHoodAppMauiAndroid Init(AppOptions options)
    {
        if (AndroidDevice.IsVpnServiceProcess) {
            VhLogger.Instance.LogInformation(
                "This is the VPN service process, skipping VpnHoodApp initialization.");
            return new VpnHoodAppMauiAndroid();
        }

        var device = AndroidDevice.Create();
        options.CultureProvider ??= AndroidAppCultureProvider.CreateIfSupported();
        VpnHoodApp.Init(device, options);
        return new VpnHoodAppMauiAndroid();
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
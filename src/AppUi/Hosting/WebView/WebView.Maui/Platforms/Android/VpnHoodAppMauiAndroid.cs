using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Android;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.AppLib.App;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppUi.Hosting.WebView.Maui;

internal class VpnHoodAppMauiAndroid : Singleton<VpnHoodAppMauiAndroid>, IVpnHoodAppMaui
{
    public static VpnHoodAppMauiAndroid Init(Func<AppOptions> optionsFactory)
    {
        if (AndroidDevice.IsVpnServiceProcess) {
            VhLogger.Instance.LogInformation(
                "This is the VPN service process, skipping VpnHoodApp initialization.");
            return new VpnHoodAppMauiAndroid();
        }

        var options = optionsFactory();
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
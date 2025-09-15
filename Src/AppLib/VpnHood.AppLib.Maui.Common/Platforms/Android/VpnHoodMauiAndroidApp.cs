using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Droid.Common;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Toolkit.Logging;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppLib.Maui.Common;

internal class VpnHoodMauiAndroidApp : IVpnHoodMauiApp
{
    public void Init(AppOptions options)
    {
        if (AndroidDevice.IsVpnServiceProcess) {
            VhLogger.Instance.LogInformation(
                "This is the VPN service process, skipping VpnHoodApp initialization.");
            return;
        }

        var device = AndroidDevice.Create();
        options.CultureProvider ??= AndroidAppCultureProvider.CreateIfSupported();
        VpnHoodApp.Init(device, options);
    }
}
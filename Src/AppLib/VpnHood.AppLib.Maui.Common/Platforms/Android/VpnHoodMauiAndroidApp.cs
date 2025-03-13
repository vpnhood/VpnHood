using VpnHood.AppLib.Droid.Common;
using VpnHood.Core.Client.Device.Droid;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppLib.Maui.Common;

internal class VpnHoodMauiAndroidApp : IVpnHoodMauiApp
{
    public VpnHoodApp Init(AppOptions options)
    {
        var device = AndroidDevice.Create();
        options.CultureProvider ??= AndroidAppCultureProvider.CreateIfSupported();
        return VpnHoodApp.Init(device, options);
    }
}
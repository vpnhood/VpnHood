using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;

// ReSharper disable once CheckNamespace
namespace VpnHood.Client.App.Maui.Common;

internal class VpnHoodMauiAndroidApp : IVpnHoodMauiApp
{
    public IDevice Device { get; } = AndroidDevice.Create();
    public IAppCultureProvider? CultureService { get; } = AndroidAppCultureProvider.CreateIfSupported();

    public void Init(VpnHoodApp vpnHoodApp)
    {
        ((AndroidDevice)vpnHoodApp.Device).InitNotification(new AndroidAppNotification(vpnHoodApp).DeviceNotification);
    }
}
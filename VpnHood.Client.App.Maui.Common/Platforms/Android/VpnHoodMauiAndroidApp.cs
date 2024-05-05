using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Maui.Common;

internal class VpnHoodMauiAndroidApp : IVpnHoodMauiApp
{
    public IDevice Device { get; } = AndroidDevice.Create();
    public IAppCultureService? CultureService { get; } = AndroidAppAppCultureService.CreateIfSupported();

    public void Init(VpnHoodApp vpnHoodApp)
    {
        ((AndroidDevice)vpnHoodApp.Device).InitNotification(new AndroidAppNotification(vpnHoodApp).DeviceNotification);

    }
}
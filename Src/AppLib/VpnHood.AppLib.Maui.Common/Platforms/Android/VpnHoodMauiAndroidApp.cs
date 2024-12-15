using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Droid.Common;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.Droid;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppLib.Maui.Common;

internal class VpnHoodMauiAndroidApp : IVpnHoodMauiApp
{
    public IDevice Device { get; } = AndroidDevice.Create();
    public IAppCultureProvider? CultureService { get; } = AndroidAppCultureProvider.CreateIfSupported();

    public void Init(VpnHoodApp vpnHoodApp)
    {
        ((AndroidDevice)vpnHoodApp.Device).InitNotification(new AndroidAppNotification(vpnHoodApp).DeviceNotification);
    }
}
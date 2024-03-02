using VpnHood.Client.Device;

namespace VpnHood.Client.App.Maui.Common;

internal interface IVpnHoodMauiApp
{
    IDevice CreateDevice();
    void Init(VpnHoodApp vpnHoodApp);
}
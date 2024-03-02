using VpnHood.Client.Device;

namespace VpnHood.Client.App.Maui.Common;

internal interface IVpnHoodMauiApp
{
    IDevice Device { get; }
    void Init(VpnHoodApp vpnHoodApp);
}
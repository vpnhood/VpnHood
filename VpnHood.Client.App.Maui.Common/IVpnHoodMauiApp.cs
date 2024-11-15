using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Maui.Common;

internal interface IVpnHoodMauiApp
{
    IDevice Device { get; }
    IAppCultureProvider? CultureService { get; }
    void Init(VpnHoodApp vpnHoodApp);
}
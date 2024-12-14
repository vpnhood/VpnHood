using VpnHood.AppFramework.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.AppFramework.Maui.Common;

internal interface IVpnHoodMauiApp
{
    IDevice Device { get; }
    IAppCultureProvider? CultureService { get; }
    void Init(VpnHoodApp vpnHoodApp);
}
using VpnHood.AppLibs.Abstractions;
using VpnHood.Core.Client.Device;

namespace VpnHood.AppLibs.Maui.Common;

internal interface IVpnHoodMauiApp
{
    IDevice Device { get; }
    IAppCultureProvider? CultureService { get; }
    void Init(VpnHoodApp vpnHoodApp);
}
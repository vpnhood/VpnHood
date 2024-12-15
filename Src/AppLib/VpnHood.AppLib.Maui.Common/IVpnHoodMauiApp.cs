using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device;

namespace VpnHood.AppLib.Maui.Common;

internal interface IVpnHoodMauiApp
{
    IDevice Device { get; }
    IAppCultureProvider? CultureService { get; }
    void Init(VpnHoodApp vpnHoodApp);
}
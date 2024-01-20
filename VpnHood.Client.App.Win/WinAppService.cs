using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Client.App.Win;

internal class WinAppService : IAppService
{
    public IDevice Device { get; } = new WinDivertDevice();
}
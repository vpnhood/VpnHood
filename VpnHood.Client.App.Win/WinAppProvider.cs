using VpnHood.Client.Device;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Client.App.Win;

internal class WinAppProvider : IAppProvider
{
    public IDevice Device { get; } = new WinDivertDevice();
    public bool IsLogToConsoleSupported => true;
}
using VpnHood.Client.Device;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Client.App
{
    class WinAppProvider : IAppProvider
    {
        public IDevice Device { get; } = new WinDivertDevice();

    }
}
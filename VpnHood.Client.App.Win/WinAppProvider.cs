using System;
using System.IO;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Client.App
{
    class WinAppProvider : IAppProvider
    {
        public IDevice Device { get; } = new WinDivertDevice();
    }
}
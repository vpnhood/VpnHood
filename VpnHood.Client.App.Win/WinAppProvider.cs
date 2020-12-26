using System;
using VpnHood.Client.Device;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Client.App
{
    class WinAppProvider : IAppProvider
    {
        public IDevice Device { get; } = new WinDivertDevice();

        public string OperatingSystemInfo => 
            Environment.OSVersion.ToString() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    }
}
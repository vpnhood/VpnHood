using VpnHood.Client.App;
using System;
using System.IO;

namespace VpnHood.Client.App
{
    class WinAppProvider : IAppProvider
    {
        private WinDivertDevice _device;

        public event EventHandler<AppDeviceReadyEventArgs> DeviceReadly;

        public void PrepareDevice()
        {
            _device = new WinDivertDevice();
            DeviceReadly?.Invoke(this, new AppDeviceReadyEventArgs(_device));
        }
    }
}
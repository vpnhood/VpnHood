using System;
using System.IO;

namespace VpnHood.Client.App
{
    public interface IAppProvider
    {
        void PrepareDevice();
        event EventHandler<AppDeviceReadyEventArgs> DeviceReadly;
    }
}
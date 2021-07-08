using System;
using System.Threading.Tasks;

namespace VpnHood.Client.Device
{
    public interface IDevice
    {
        event EventHandler OnStartAsService;
        Task<IPacketCapture> CreatePacketCapture();
        string OperatingSystemInfo { get; }
        DeviceAppInfo[] InstalledApps { get; }
        bool IsExcludeAppsSupported { get; }
        bool IsIncludeAppsSupported { get; }
    }

}

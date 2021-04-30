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
        bool IsExcludeApplicationsSupported { get; }
        bool IsIncludeApplicationsSupported { get; }
        bool IsExcludeNetworksSupported { get; }
        bool IsIncludeNetworksSupported { get; }
    }

}

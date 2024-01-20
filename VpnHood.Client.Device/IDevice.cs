namespace VpnHood.Client.Device;

public interface IDevice
{
    string OsInfo { get; }
    DeviceAppInfo[] InstalledApps { get; }
    bool IsExcludeAppsSupported { get; }
    bool IsIncludeAppsSupported { get; }
    event EventHandler OnStartAsService;
    Task<IPacketCapture> CreatePacketCapture();
}
namespace VpnHood.Core.Client.Device;

public interface IDevice : IDisposable
{
    bool IsExcludeAppsSupported { get; }
    bool IsIncludeAppsSupported { get; }
    bool IsAlwaysOnSupported { get; }
    string OsInfo { get; }
    DeviceMemInfo? MemInfo { get; }
    DeviceAppInfo[] InstalledApps { get; }
    Task<IVpnAdapter> CreateVpnAdapter(IUiContext? uiContext);
}
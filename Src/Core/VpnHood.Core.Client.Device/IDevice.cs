namespace VpnHood.Core.Client.Device;

public interface IDevice : IAsyncDisposable
{
    string VpnServiceConfigFolder { get; }
    bool IsExcludeAppsSupported { get; }
    bool IsIncludeAppsSupported { get; }
    bool IsAlwaysOnSupported { get; }
    string OsInfo { get; }
    DeviceMemInfo? MemInfo { get; }
    DeviceAppInfo[] InstalledApps { get; }
    Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken);
    Task StartVpnService(CancellationToken cancellationToken);
}
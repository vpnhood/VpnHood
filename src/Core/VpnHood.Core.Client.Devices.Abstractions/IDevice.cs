using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

namespace VpnHood.Core.Client.Devices.Abstractions;

public interface IDevice : IDisposable
{
    // The VPN service's own files - its options, status and log, and the split filters the app
    // publishes for it - on this device only: never in a backup or a transfer to a new device.
    string VpnServiceConfigFolder { get; }

    // What the app and the VPN service share that is the person's own and travels with a backup: the
    // proxies they added (VpnServiceFolders). The config folder where the OS backs up neither.
    string VpnServiceDataFolder { get; }
    bool IsTv { get; }
    bool IsExcludeAppsSupported { get; }
    bool IsIncludeAppsSupported { get; }
    bool IsBindProcessToVpnSupported { get; }
    bool IsQuicSupported { get; }

    // Whether the app keeps always on itself (the app's AlwaysOn setting): a desktop's service, which
    // starts at boot. Android's is the system's, which the app only opens.
    bool IsAlwaysOnSupported { get; }
    string OsInfo { get; }
    DeviceUserAgentInfo UserAgentInfo { get; }
    DeviceMemInfo? MemInfo { get; }
    IReadOnlyList<DeviceAppInfo> InstalledApps { get; }
    Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken);
    Task StartVpnService(CancellationToken cancellationToken);
    IMessageClient CreateMessageClient();
    void BindProcessToVpn(bool value);
}
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

namespace VpnHood.Core.Client.Devices;

public interface IDevice : IDisposable
{
    string VpnServiceConfigFolder { get; }
    bool IsTv { get; }
    bool IsExcludeAppsSupported { get; }
    bool IsIncludeAppsSupported { get; }
    bool IsBindProcessToVpnSupported { get; }
    bool IsTcpProxySupported { get; }
    bool IsQuicSupported { get; }
    string OsInfo { get; }
    DeviceMemInfo? MemInfo { get; }
    DeviceAppInfo[] InstalledApps { get; }
    Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken);
    Task StartVpnService(CancellationToken cancellationToken);
    IMessageClient CreateMessageClient();
    void BindProcessToVpn(bool value);
}
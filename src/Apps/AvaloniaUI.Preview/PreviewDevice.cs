using VpnHood.Core.Client.Devices;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

namespace VpnHood.App.AvaloniaUI.Preview;

// The Windows device, reporting itself as a TV when the preview is run as one. IsTv is the
// device's word (AppFeatures.IsTv reads it, or the /tv-mode debug command at the next launch), and
// the preview wants the answer for this run, from its command line.
internal sealed class PreviewDevice(IDevice inner, bool isTv) : IDevice
{
    public bool IsTv => isTv;
    public string VpnServiceConfigFolder => inner.VpnServiceConfigFolder;
    public bool IsExcludeAppsSupported => inner.IsExcludeAppsSupported;
    public bool IsIncludeAppsSupported => inner.IsIncludeAppsSupported;
    public bool IsBindProcessToVpnSupported => inner.IsBindProcessToVpnSupported;
    public bool IsTcpProxySupported => inner.IsTcpProxySupported;
    public bool IsQuicSupported => inner.IsQuicSupported;
    public string OsInfo => inner.OsInfo;
    public DeviceMemInfo? MemInfo => inner.MemInfo;
    public IReadOnlyList<DeviceAppInfo> InstalledApps => inner.InstalledApps;

    public Task RequestVpnService(IUiContext? uiContext, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return inner.RequestVpnService(uiContext, timeout, cancellationToken);
    }

    public Task StartVpnService(CancellationToken cancellationToken)
    {
        return inner.StartVpnService(cancellationToken);
    }

    public IMessageClient CreateMessageClient()
    {
        return inner.CreateMessageClient();
    }

    public void BindProcessToVpn(bool value)
    {
        inner.BindProcessToVpn(value);
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}

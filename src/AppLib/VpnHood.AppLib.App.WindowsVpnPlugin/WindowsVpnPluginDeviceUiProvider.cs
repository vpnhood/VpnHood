using VpnHood.AppLib.Abstractions.Device;

namespace VpnHood.AppLib.App.WindowsVpnPlugin;

// WindowsDeviceUiProvider's proxy reader, copied: its project brings WinTun and WinDivert.
public class WindowsVpnPluginDeviceUiProvider : NullDeviceUiProvider
{
    public override bool IsProxySettingsSupported => true;
    private readonly Uri _internetUrl = new("https://www.microsoft.com");

    public override DeviceProxySettings? GetProxySettings()
    {
        var proxyUrl = HttpClient.DefaultProxy.GetProxy(_internetUrl);
        if (proxyUrl == null)
            return null;

        return new DeviceProxySettings {
            ProxyUrl = proxyUrl,
            PacFileUrl = null,
            ExcludeDomains = []
        };
    }
}

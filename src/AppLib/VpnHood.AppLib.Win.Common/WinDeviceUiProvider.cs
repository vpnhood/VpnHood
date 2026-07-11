using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Win.Common;

public class WinDeviceUiProvider : NullDeviceUiProvider
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
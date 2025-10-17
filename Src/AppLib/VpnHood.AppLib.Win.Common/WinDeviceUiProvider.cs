using System.Net;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Win.Common;

public class WinDeviceUiProvider : NullDeviceUiProvider
{
   public override bool IsProxySettingsSupported => true;

    public override DeviceProxySettings? GetProxySettings()
    {
        var proxyUrl = WebRequest.DefaultWebProxy?.GetProxy(new Uri("https://www.microsoft.com"));
        if (proxyUrl == null)
            return null;

        return new DeviceProxySettings {
            Host = proxyUrl.Host,
            Port = proxyUrl.Port,
            PacFileUrl = null,
            ExcludeDomains = []
        };
    }
}
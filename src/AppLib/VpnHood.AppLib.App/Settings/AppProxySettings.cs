using VpnHood.Core.Proxies.Management.Abstractions.Options;

namespace VpnHood.AppLib.Settings;

public class AppProxySettings
{
    public AppProxyMode Mode { get; set; }
    public ProxyAutoUpdateOptions AutoUpdateOptions { get; init; } = new();
}
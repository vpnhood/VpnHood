using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;

namespace VpnHood.AppLib.Settings;

public class AppProxySettings
{
    public AppProxyMode Mode { get; set; }
    public ProxyAutoUpdateOptions EndPointProxyAutoUpdateOptions { get; init; } = new();

}
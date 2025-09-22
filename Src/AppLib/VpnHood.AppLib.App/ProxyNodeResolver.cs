using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib;

//todo: what is the usage?
public static class ProxyNodeResolver
{
    public static ProxyNode[] Resolve(AppProxySettings proxySettings)
    {
        return proxySettings.Mode switch {
            AppProxyMode.Disabled => [],
            AppProxyMode.System => [],
            AppProxyMode.Custom => proxySettings.Nodes,
            _ => []
        };
    }
}
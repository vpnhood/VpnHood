using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib;

public static class ProxyNodeResolver
{
    public static ProxyNode[] Resolve(AppProxySettings appProxySettings)
    {
        return appProxySettings.Mode switch {
            ProxyMode.Disabled => [],
            ProxyMode.System => [],
            ProxyMode.Remote => [],
            ProxyMode.Custom => appProxySettings.Nodes,
            _ => []
        };
    }
}
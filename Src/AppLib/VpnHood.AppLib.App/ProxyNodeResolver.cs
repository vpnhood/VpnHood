using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib;

public static class ProxyNodeResolver
{
    public static ProxyNode[] Resolve(ProxySettings proxySettings)
    {
        return proxySettings.Mode switch {
            ProxyMode.Disabled => [],
            ProxyMode.System => [],
            ProxyMode.Remote => [],
            ProxyMode.Custom => proxySettings.Nodes,
            _ => []
        };
    }
}
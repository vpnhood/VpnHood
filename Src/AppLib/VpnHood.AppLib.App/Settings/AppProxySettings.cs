using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib.Settings;

public class AppProxySettings
{
    public ProxyMode Mode { get; set; }
    public ProxyNode[] Nodes { get; set; } = [];
}
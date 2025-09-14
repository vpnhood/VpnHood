using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.Settings;

public class ProxySettings
{
    public ProxyMode Mode { get; set; }
    public ProxyNode[] Nodes { get; set; } = [];
}
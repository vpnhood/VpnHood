using VpnHood.Core.Proxies.EndPointManagement;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorEndPointInfo
{
    public required ProxyEndPointManager ProxyEndPointManager { get; init; }
    public required ServerFinderItem ServerFinderItem { get; init; }
}
using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorServiceOptions
{
    public required VpnEndPoint VpnEndPoint { get; init; }
    public required ProxyEndPointManager ProxyEndPointManager { get; init; }
    public required ISocketFactory SocketFactory { get; init; }
    public required TimeSpan RequestTimeout { get; init; }
    public required bool AllowStreamReuse { get; init; }
}
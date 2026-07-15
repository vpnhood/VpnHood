using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorServiceOptions
{
    public required VpnEndPoint VpnEndPoint { get; init; }
    public required IProxyConnector? ProxyConnector { get; init; }
    public required ISocketFactory SocketFactory { get; init; }
    public required TimeSpan RequestTimeout { get; init; }
    public required bool AllowChannelReuse { get; init; }
}

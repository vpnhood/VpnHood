using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Client.ConnectorServices;

internal record ConnectorServiceOptions(
    VpnEndPoint VpnEndPoint,
    ProxyEndPointManager ProxyEndPointManager,
    ISocketFactory SocketFactory,
    bool AllowTcpReuse
);
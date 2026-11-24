using System.Net.Sockets;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Core.Proxies.Management.Checks;

// the factory and server of a check through which a proxy reached the server, or of the connection that did
internal sealed record LastCheck(ISocketFactory SocketFactory, CheckTarget Target, TcpClient? Connection = null);

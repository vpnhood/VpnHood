using System.Net;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Server;

internal class ServerConnection(IConnection connection) : ConnectionDecorator(connection)
{
    // it may be different from RemoteEndPoint.Address due to proxying
    public required IPAddress ClientIp { get; init; }
    public bool IsReverseProxy { get; init; }
}
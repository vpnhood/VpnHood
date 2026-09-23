using System.Net;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Server;

internal class ServerStreamConnection(IStreamConnection streamConnection) 
    : StreamConnectionDecorator(streamConnection)
{
    // it may be different from RemoteEndPoint.Address due to proxying
    public required IPAddress ClientIp { get; init; }
    public bool IsReverseProxy { get; init; }
}
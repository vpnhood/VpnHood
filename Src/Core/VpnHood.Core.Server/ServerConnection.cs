using System.Net;
using VpnHood.Core.Tunneling.ClientStreams;

namespace VpnHood.Core.Server;

internal struct ServerConnection
{
    public required IClientStream ClientStream { get; init; }
    public required IPAddress ClientIp { get; init; }
}
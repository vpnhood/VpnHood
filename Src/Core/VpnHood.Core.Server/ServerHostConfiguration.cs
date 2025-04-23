using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Core.Server;

internal class ServerHostConfiguration
{
    public required IPEndPoint[] TcpEndPoints { get; init; } = [];
    public required IPEndPoint[] UdpEndPoints { get; init; } = [];
    public required IPAddress[]? DnsServers { get; init; }
    public required X509Certificate2[] Certificates { get; init; } = [];
    public required int UdpSendBufferSize { get; init; } 
    public required int UdpReceiveBufferSize { get; init; } 
}
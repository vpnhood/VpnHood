using System.Net;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server;

internal class ServerHostConfiguration
{
    public required IPEndPoint[] TcpEndPoints { get; init; } = [];
    public required IPEndPoint[] UdpEndPoints { get; init; } = [];
    public required IPAddress[]? DnsServers { get; init; }
    public required X509Certificate2[] Certificates { get; init; } = [];
    public required TransferBufferSize? UdpChannelBufferSize { get; init; } 
}
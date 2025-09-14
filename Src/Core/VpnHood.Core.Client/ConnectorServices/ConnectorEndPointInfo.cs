using System.Net;
using VpnHood.Core.Client.ProxyNodes;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorEndPointInfo
{
    public required ProxyNodeManager ProxyNodeManager { get; init; }
    public required IPEndPoint TcpEndPoint { get; init; }
    public required string HostName { get; init; }
    public required byte[]? CertificateHash { get; init; }
}
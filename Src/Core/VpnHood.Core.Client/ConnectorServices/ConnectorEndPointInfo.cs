using System.Net;
using VpnHood.Core.Proxies.EndPointManagement;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorEndPointInfo
{
    public required ProxyEndPointManager ProxyEndPointManager { get; init; }
    public required IPEndPoint TcpEndPoint { get; init; }
    public required string HostName { get; init; }
    public required byte[]? CertificateHash { get; init; }
}
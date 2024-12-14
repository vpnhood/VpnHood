using System.Net;

namespace VpnHood.Client.ConnectorServices;

internal class ConnectorEndPointInfo
{
    public required IPEndPoint TcpEndPoint { get; set; }
    public required string HostName { get; set; }
    public required byte[]? CertificateHash { get; set; }
}
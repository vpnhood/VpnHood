using System.Net;

namespace VpnHood.Core.Client;

public record VpnEndPoint(
    IPEndPoint TcpEndPoint,
    string HostName,
    byte[]? CertificateHash,
    string? PathBase);
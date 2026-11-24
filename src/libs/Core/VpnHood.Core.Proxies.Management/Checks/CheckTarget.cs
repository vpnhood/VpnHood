using System.Net;
using System.Net.Security;

namespace VpnHood.Core.Proxies.Management.Checks;

// a VPN server the proxies are checked against: its endpoint, the name its TLS takes, and the check of its certificate
internal sealed record CheckTarget(
    IPEndPoint EndPoint,
    string? HostName,
    RemoteCertificateValidationCallback? CertificateValidation);

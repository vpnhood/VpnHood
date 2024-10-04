using VpnHood.AccessServer.Providers.Acme;

namespace VpnHood.AccessServer.Dtos.Certificates;

public class CertificateCreateParams
{
    public required CertificateSigningRequest CertificateSigningRequest { get; init; }
    public DateTime? ExpirationTime { get; set; }
}
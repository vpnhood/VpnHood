namespace VpnHood.AccessServer.Dtos.Certificate;

public class CertificateSelfSignedParams
{
    public required CertificateSigningRequest CertificateSigningRequest { get; init; }
    public DateTime? ExpirationTime { get; set; }
}
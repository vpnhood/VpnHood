namespace VpnHood.AccessServer.Dtos;

public class CertificateSelfSignedParams
{
    public required CertificateSigningRequest CertificateSigningRequest { get; init; }
    public DateTime? ExpirationTime { get; set; }
}
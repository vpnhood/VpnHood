namespace VpnHood.AccessServer.Dtos.Certificate;

public class CertificateCreateParams
{
    public required CertificateSigningRequest CertificateSigningRequest { get; init; }
    public DateTime? ExpirationTime { get; set; }
}
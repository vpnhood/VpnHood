namespace VpnHood.AccessServer.Dtos;

public class CertificateData
{
    public required Certificate Certificate { get; set; }
    public CertificateSummary? Summary { get; set; }
}
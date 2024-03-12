namespace VpnHood.Server.Access.Configurations;

public class CertificateData
{
    public required string CommonName { get; init; }
    public required byte[] RawData { get; init; }
}
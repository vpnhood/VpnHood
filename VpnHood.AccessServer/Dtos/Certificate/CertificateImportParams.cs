namespace VpnHood.AccessServer.Dtos.Certificate;

public class CertificateImportParams
{
    public required byte[] RawData { get; set; }
    public string? Password { get; set; }
}
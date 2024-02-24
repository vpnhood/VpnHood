namespace VpnHood.AccessServer.Dtos.Certificates;

public class CertificateImportParams
{
    public required byte[] RawData { get; set; }
    public string? Password { get; set; }
}
namespace VpnHood.AccessServer.Dtos;

public class CertificateCreateParams
{
    public string? SubjectName { get; set; }
    public byte[]? RawData { get; set; }
    public string? Password { get; set; }
}
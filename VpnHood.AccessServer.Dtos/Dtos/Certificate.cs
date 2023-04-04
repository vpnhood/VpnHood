using System;

namespace VpnHood.AccessServer.Dtos;

public class Certificate
{
    public Guid CertificateId { get; set; }
    public string CommonName { get; set; } = null!;
    public DateTime ExpirationTime { get; set; }
    public DateTime CreatedTime { get; set; }
    public byte[]? RawData { get; set; }
}
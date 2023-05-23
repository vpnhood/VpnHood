using System;

namespace VpnHood.AccessServer.Dtos;

public class Certificate
{
    public required Guid CertificateId { get; set; }
    public required string CommonName { get; set; }
    public required DateTime? IssueTime { get; set; }
    public required DateTime ExpirationTime { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required bool IsVerified { get; set; }
    public byte[]? RawData { get; set; }
}
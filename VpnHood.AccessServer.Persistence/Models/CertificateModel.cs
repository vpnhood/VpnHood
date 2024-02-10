namespace VpnHood.AccessServer.Persistence.Models;

public class CertificateModel
{
    public required Guid CertificateId { get; set; }
    public required Guid ProjectId { get; set; }
    public required string CommonName { get; set; }
    public required string SubjectName { get; set; }
    public required byte[] RawData { get; set; }
    public required DateTime IssueTime { get; set; } 
    public required DateTime ExpirationTime { get; set; }
    public required string Thumbprint { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required bool IsVerified { get; set; }
    public required string? DnsVerificationText { get; set; }
    public bool IsDeleted { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ICollection<ServerFarmModel>? ServerFarms { get; set; }
}
namespace VpnHood.AccessServer.Models;

public class CertificateModel
{
    public Guid CertificateId { get; set; }
    public Guid ProjectId { get; set; }
    public string CommonName { get; set; } = default!;
    public byte[] RawData { get; set; } = null!;
    public DateTime? IssueTime { get; set; } //todo must not null
    public DateTime ExpirationTime { get; set; }
    public string? Thumbprint { get; set; } //todo must not null
    public DateTime CreatedTime { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsVerified { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ICollection<ServerFarmModel>? ServerFarms { get; set; }
}
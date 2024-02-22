namespace VpnHood.AccessServer.Persistence.Models;

public class CertificateModel
{
    public required Guid CertificateId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required string CommonName { get; set; }
    public required string SubjectName { get; set; }
    public required byte[] RawData { get; set; }
    public required DateTime IssueTime { get; set; } 
    public required DateTime ExpirationTime { get; set; }
    public required string Thumbprint { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required bool IsTrusted { get; set; }
    public required bool AutoRenew { get; set; }
    public required int RenewCount { get; set; }
    public required bool RenewInprogress { get; set; }
    public required int RenewErrorCount { get; set; }
    public required string? RenewError { get; set; }
    public required DateTime? RenewErrorTime { get; set; }
    public required string? RenewToken { get; set; }
    public required string? RenewKeyAuthorization { get; set; }
    public required bool IsDefault { get; set; }
    public required bool IsDeleted { get; set; }

    public virtual ServerFarmModel? ServerFarm { get; set; }
    public virtual ProjectModel? Project { get; set; }
}
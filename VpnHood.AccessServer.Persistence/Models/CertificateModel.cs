namespace VpnHood.AccessServer.Persistence.Models;

public class CertificateModel
{
    public required Guid CertificateId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required string CommonName { get; set; }
    public required byte[] RawData { get; set; }
    public required DateTime IssueTime { get; set; }
    public required DateTime ExpirationTime { get; set; }
    public required string Thumbprint { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required bool IsValidated { get; set; }
    public required bool AutoValidate { get; set; }
    public required int ValidateCount { get; set; }
    public required bool ValidateInprogress { get; set; }
    public required string? ValidateToken { get; set; }
    public required string? ValidateKeyAuthorization { get; set; }
    public required int ValidateErrorCount { get; set; }
    public required string? ValidateError { get; set; }
    public required DateTime? ValidateErrorTime { get; set; }
    public required bool IsInToken { get; set; }
    public required bool IsDeleted { get; set; }

    public virtual ServerFarmModel? ServerFarm { get; set; }
    public virtual ProjectModel? Project { get; set; }
}
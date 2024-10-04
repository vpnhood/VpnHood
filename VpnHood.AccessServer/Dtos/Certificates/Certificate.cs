namespace VpnHood.AccessServer.Dtos.Certificates;

public class Certificate
{
    public required Guid CertificateId { get; init; }
    public required bool IsInToken { get; init; }
    public required string CommonName { get; init; }
    public required DateTime IssueTime { get; init; }
    public required DateTime ExpirationTime { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required string Thumbprint { get; init; }
    public required bool IsValidated { get; init; }
    public required bool AutoValidate { get; init; }
    public required int ValidateCount { get; init; }
    public required bool ValidateInprogress { get; set; }
    public required int ValidateErrorCount { get; init; }
    public required string? ValidateError { get; init; }
    public required DateTime? ValidateErrorTime { get; init; }
}
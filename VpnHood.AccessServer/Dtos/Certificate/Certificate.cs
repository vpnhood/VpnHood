namespace VpnHood.AccessServer.Dtos.Certificate;

public class Certificate
{
    public required Guid CertificateId { get; init; }
    public required string CommonName { get; init; }
    public required string SubjectName { get; init; }
    public required DateTime IssueTime { get; init; }
    public required DateTime ExpirationTime { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required string Thumbprint { get; init; }
    public required bool IsTrusted { get; init; }
    public required bool AutoRenew { get; init; }
    public required int RenewCount { get; init; }
    public required bool RenewInprogress { get; init; }
    public required int RenewErrorCount { get; init; }
    public required string? RenewError { get; init; }
    public required DateTime? RenewErrorTime { get; init; }
}
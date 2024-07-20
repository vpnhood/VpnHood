namespace VpnHood.AccessServer.Persistence.Models;

public class ServerFarmModel
{
    public required Guid ServerFarmId { get; set; }
    public required string ServerFarmName { get; set; }
    public required Guid ProjectId { get; set; }
    public required Guid ServerProfileId { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required bool UseHostName { get; set; }
    public required byte[] Secret { get; set; }
    public required string? TokenJson { get; set; }
    public required string? TokenUrl { get; set; }
    public required bool PushTokenToClient { get; set; }
    public required string? TokenError { get; set; }
    public required int MaxCertificateCount { get; set; }
    public bool IsDeleted { get; set; } = false;

    public virtual ServerProfileModel? ServerProfile { get; set; }
    public virtual ProjectModel? Project { get; set; }
    public virtual ICollection<CertificateModel>? Certificates { get; set; }
    public virtual ICollection<AccessTokenModel>? AccessTokens { get; set; }
    public virtual ICollection<ServerModel>? Servers { get; set; }
}
namespace VpnHood.AccessServer.Persistence.Models;

public class FarmTokenRepoModel
{
    public required Guid FarmTokenRepoId { get; init; }
    public required string FarmTokenRepoName { get; set; }
    public required Guid ProjectId { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required Uri PublishUrl { get; set; }
    public required Uri? UploadUrl { get; set; }
    public required string HttpMethod { get; set; }
    public required string? AuthorizationKey { get; set; }
    public required string? AuthorizationValue { get; set; }
    public required string? Error { get; set; }
    public required DateTime? UploadedTime { get; set; }
    public required bool IsPendingUpload { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ServerFarmModel? ServerFarm { get; set; }
}
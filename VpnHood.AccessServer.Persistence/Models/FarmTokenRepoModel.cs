using System.Text.Json;
using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Persistence.Models;

public class FarmTokenRepoModel
{
    public required Guid FarmTokenRepoId { get; init; }
    public required string FarmTokenRepoName { get; set; }
    public required Guid ProjectId { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required Uri PublishUrl { get; set; }
    public required string? Error { get; set; }
    public required DateTime? UploadedTime { get; set; }
    public required bool IsPendingUpload { get; set; }
    public required string? RepoSettings { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ServerFarmModel? ServerFarm { get; set; }
    public virtual FarmTokenRepoSettings? GetRepoSettings() => 
        RepoSettings == null ? null : GmUtil.JsonDeserialize<FarmTokenRepoSettings>(RepoSettings);

}
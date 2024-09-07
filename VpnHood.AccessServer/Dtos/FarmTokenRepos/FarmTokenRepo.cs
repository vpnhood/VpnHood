using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Dtos.FarmTokenRepos;

public class FarmTokenRepo
{
    public required string FarmTokenRepoId { get; init; }
    public required string FarmTokenRepoName { get; set; }
    public required Uri PublishUrl { get; set; }
    public required FarmTokenRepoSettings? RepoSettings { get; set; }
    public required bool? IsUpToDate { get; set; }
    public required string? Error { get; set; }
}
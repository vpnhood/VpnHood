using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Dtos.FarmTokenRepos;

public class FarmTokenRepoCreateParams
{
    public required string RepoName { get; set; }
    public required Uri PublishUrl { get; set; }
    public required FarmTokenRepoSettings? RepoSettings { get; set; }

}
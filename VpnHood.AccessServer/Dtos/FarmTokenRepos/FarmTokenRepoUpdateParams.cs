using GrayMint.Common.Utils;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Dtos.FarmTokenRepos;

public class FarmTokenRepoUpdateParams
{
    public Patch<string>? RepoName { get; set; }
    public Patch<Uri>? PublishUrl { get; set; }
    public Patch<FarmTokenRepoSettings?>? RepoSettings { get; set; }
}

using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.FarmTokenRepos;

public class FarmTokenRepoUpdateParams
{
    public Patch<string>? RepoName { get; set; }
    public Patch<Uri>? PublishUrl { get; set; }
    public Patch<Uri?>? UploadUrl { get; set; }
    public Patch<string>? UploadMethod { get; set; } 
    public Patch<string?>? AuthorizationKey { get; set; }
    public Patch<string?>? AuthorizationValue { get; set; }
}

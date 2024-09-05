namespace VpnHood.AccessServer.Dtos.FarmTokenRepos;

public class FarmTokenRepoCreateParams
{
    public required string RepoName { get; set; }
    public required Uri? UploadUrl { get; set; }
    public required string HttpMethod { get; set; } = "PUT";
    public required string? AuthorizationKey { get; set; }
    public required string? AuthorizationValue { get; set; }
    public required Uri PublishUrl { get; set; }
}
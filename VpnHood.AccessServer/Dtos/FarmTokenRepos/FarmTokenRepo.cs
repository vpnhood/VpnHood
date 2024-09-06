namespace VpnHood.AccessServer.Dtos.FarmTokenRepos;

public class FarmTokenRepo
{
    public required string FarmTokenRepoId { get; init; }
    public required string FarmTokenRepoName { get; set; }
    public required Uri PublishUrl { get; set; }
    public required Uri? UploadUrl { get; set; }
    public required string UploadMethod { get; set; }
    public required bool? IsUpToDate { get; set; }
    public required string? AuthorizationKey { get; set; }
    public required string? AuthorizationValue { get; set; }
    public required string? Error { get; set; }
}
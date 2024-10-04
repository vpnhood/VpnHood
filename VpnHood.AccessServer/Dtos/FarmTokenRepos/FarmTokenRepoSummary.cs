namespace VpnHood.AccessServer.Dtos.FarmTokenRepos;

public class FarmTokenRepoSummary
{
    public required string[] UpToDateRepoNames { get; init; }
    public required string[] OutdatedRepoNames { get; init; }
    public required int Total { get; init; }
}
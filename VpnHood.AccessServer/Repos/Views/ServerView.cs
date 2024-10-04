using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Repos.Views;

public class ServerView
{
    public required ServerModel Server { get; init; }
    public required string ClientFilterName { get; init; }
    public required string ServerFarmName { get; init; }
}
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Persistence.Views;

public class AccessTokenView
{
    public required string ServerFarmName { get; init; }
    public required AccessTokenModel AccessToken { get; init; }
    public required AccessModel? Access { get; init; }
}
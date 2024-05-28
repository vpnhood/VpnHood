using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Repos.Views;

public class ServerFarmView
{
    public required  ServerFarmModel ServerFarm { get; init; }
    public required string ServerProfileName { get; init; }
    public required CertificateModel Certificate { get; init; }
    public required int? ServerCount { get; init; }
    public required AccessTokenView[]? AccessTokens { get; init; }

    public class AccessTokenView
    {
        public required DateTime? FirstUsedTime { get; init; }
        public required DateTime? LastUsedTime { get; init; }
    }

}


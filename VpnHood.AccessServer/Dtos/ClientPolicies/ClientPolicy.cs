
namespace VpnHood.AccessServer.Dtos.ClientPolicies;

public class ClientPolicy
{
    public required string CountryCode { get; init; }
    public string[]? FreeLocations { get; init; }
    public bool AutoLocationOnly { get; init; }
    public bool UnblockableOnly { get; init; }
    public int? Normal { get; init; }
    public int? PremiumByTrial { get; init; }
    public int? PremiumByRewardAd { get; init; }
    public bool PremiumByPurchase { get; init; }
    public bool PremiumByCode { get; init; }
}
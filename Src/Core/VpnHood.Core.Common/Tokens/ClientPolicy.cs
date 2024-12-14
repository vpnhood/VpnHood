using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Tokens;

public class ClientPolicy
{
    [JsonPropertyName("ccs")]
    public required string[] ClientCountries { get; init; }

    [JsonPropertyName("free")]
    public string[]? FreeLocations { get; init; }

    [JsonPropertyName("ao")]
    public bool AutoLocationOnly { get; init; }

    [JsonPropertyName("uo")]
    public bool UnblockableOnly { get; init; }

    [JsonPropertyName("n")]
    public int? Normal { get; init; }

    [JsonPropertyName("pbt")]
    public int? PremiumByTrial { get; init; }

    [JsonPropertyName("pbr")]
    public int? PremiumByRewardedAd { get; init; }
    
    [JsonPropertyName("pbe")]
    public bool CanExtendPremiumByAd { get; init; }

    [JsonPropertyName("pbp")]
    public bool PremiumByPurchase { get; init; }

    [JsonPropertyName("pbc")]
    public bool PremiumByCode { get; init; }
}
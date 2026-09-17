using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib.Contracts.ClientProfiles;

// What the operator that issued this token allows the people of a set of countries to do. The short
// JSON names are the token's own - they travel inside the access key, so they are not ours to
// rename.
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

    [JsonPropertyName("nbr")]
    public int? NormalByRewardedAd { get; init; }

    [JsonPropertyName("pbt")]
    public int? PremiumByTrial { get; init; }

    [JsonPropertyName("pbtdl")]
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? PremiumByTrialDailyLimit { get; init; }

    [JsonPropertyName("pbr")]
    public int? PremiumByRewardedAd { get; init; }

    [JsonPropertyName("pbe")]
    public bool CanExtendPremiumByAd { get; init; }

    [JsonPropertyName("pbp")]
    public bool PremiumByPurchase { get; init; }

    [JsonPropertyName("pbc")]
    public bool PremiumByCode { get; init; }

    /// <summary>
    /// The shop this operator sells through. Naming one REPLACES the app's own store: the client
    /// shows this page and nothing else, so an in-app store and an outside shop are never offered
    /// side by side. A build shipped through a store ignores it outright.
    /// </summary>
    [JsonPropertyName("pur_url")]
    public Uri? PurchaseUrl { get; init; }
}

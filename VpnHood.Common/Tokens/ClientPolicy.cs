using System.Text.Json.Serialization;

namespace VpnHood.Common.Tokens;

public class ClientPolicy
{
    [JsonPropertyName("cc")]
    public required string CountryCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("free")]
    public string[]? FreeLocations { get; init; }

    [JsonPropertyName("ao")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoLocationOnly { get; init; }

    [JsonPropertyName("n")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? Normal { get; init; }

    [JsonPropertyName("pbt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? PremiumByTrial { get; init; }

    [JsonPropertyName("pbr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? PremiumByRewardAd { get; init; }

    [JsonPropertyName("pbp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PremiumByPurchase { get; init; }

    [JsonPropertyName("pbc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PremiumByCode { get; init; }
}
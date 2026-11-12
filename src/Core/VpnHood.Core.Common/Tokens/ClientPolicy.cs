using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Tokens;

/// <summary>
/// Represents what an access key's issuer offers the people of a set of countries: free and premium plans, and which
/// locations they may pick.
/// </summary>
public class ClientPolicy
{
    /// <summary>Gets the country codes the policy is for; <c>*</c> stands for any country with no policy of its own.</summary>
    [JsonPropertyName("ccs")]
    public required string[] ClientCountries { get; init; }

    /// <summary>Gets the country codes of the locations free connections may use, or <see langword="null"/>.</summary>
    [JsonPropertyName("free")]
    public string[]? FreeLocations { get; init; }

    /// <summary>Gets a value indicating whether only the automatic location may be picked.</summary>
    [JsonPropertyName("ao")]
    public bool AutoLocationOnly { get; init; }

    /// <summary>Gets a value indicating whether only locations of unblockable servers are offered.</summary>
    [JsonPropertyName("uo")]
    public bool UnblockableOnly { get; init; }

    /// <summary>
    /// Gets the minutes of a free connection, 0 for no limit, or <see langword="null"/> where none is offered
    /// (<see cref="ConnectPlanId.Normal"/>).
    /// </summary>
    [JsonPropertyName("n")]
    public int? Normal { get; init; }

    /// <summary>
    /// Gets the minutes of a free connection after a rewarded ad, 0 for no limit, or <see langword="null"/> where none
    /// is offered (<see cref="ConnectPlanId.NormalByRewardedAd"/>).
    /// </summary>
    // ReSharper disable once StringLiteralTypo
    [JsonPropertyName("nbr")]
    public int? NormalByRewardedAd { get; init; }

    /// <summary>
    /// Gets the minutes of a premium trial, 0 for no limit, or <see langword="null"/> where none is offered
    /// (<see cref="ConnectPlanId.PremiumByTrial"/>).
    /// </summary>
    [JsonPropertyName("pbt")]
    public int? PremiumByTrial { get; init; }

    /// <summary>Gets how much premium trial a day allows, or <see langword="null"/> for no daily limit.</summary>
    // ReSharper disable once StringLiteralTypo
    [JsonPropertyName("pbtdl")]
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? PremiumByTrialDailyLimit { get; init; }

    /// <summary>
    /// Gets the minutes of premium a rewarded ad gives, 0 for no limit, or <see langword="null"/> where none is offered
    /// (<see cref="ConnectPlanId.PremiumByRewardedAd"/>).
    /// </summary>
    [JsonPropertyName("pbr")]
    public int? PremiumByRewardedAd { get; init; }

    /// <summary>
    /// Gets a value indicating whether a premium session can be extended by a rewarded ad; no client or server acts on
    /// it yet.
    /// </summary>
    [JsonPropertyName("pbe")]
    public bool CanExtendPremiumByAd { get; init; }

    /// <summary>Gets a value indicating whether premium can be bought.</summary>
    [JsonPropertyName("pbp")]
    public bool PremiumByPurchase { get; init; }

    /// <summary>Gets a value indicating whether a premium code can be entered.</summary>
    [JsonPropertyName("pbc")]
    public bool PremiumByCode { get; init; }

    /// <summary>
    /// The shop this operator sells through. Naming one REPLACES the app's own store: the client
    /// shows this page and nothing else, so an in-app store and an outside shop are never offered
    /// side by side. A build shipped through a store ignores it outright — where the link may be
    /// shown is a property of the BUILD, never of a country or a server.
    /// </summary>
    [JsonPropertyName("pur_url")]
    public Uri? PurchaseUrl { get; init; }
}
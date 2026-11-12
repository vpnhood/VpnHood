using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.VpnProfiles;

// What the operator that issued this token allows the people of a set of countries to do. The short
// JSON names are the token's own - they travel inside the access key, so they are not ours to
// rename.

/// <summary>
/// Represents what an access key's issuer offers the people of a set of countries: free and premium plans, and which
/// locations they may pick.
/// </summary>
public class ClientPolicy
{
    /// <summary>Gets the country codes the policy is for; <c>*</c> stands for any country with no policy of its own.</summary>
    [JsonPropertyName("ccs")]
    public required IReadOnlyList<string> ClientCountries { get; init; }

    /// <summary>Gets the country codes of the locations free connections may use, or <see langword="null"/>.</summary>
    [JsonPropertyName("free")]
    public IReadOnlyList<string>? FreeLocations { get; init; }

    /// <summary>Gets a value indicating whether only the automatic location may be picked.</summary>
    [JsonPropertyName("ao")]
    public bool AutoLocationOnly { get; init; }

    /// <summary>Gets a value indicating whether only locations of unblockable servers are offered.</summary>
    [JsonPropertyName("uo")]
    public bool UnblockableOnly { get; init; }

    /// <summary>
    /// Gets the minutes of an ordinary connection, 0 for no limit, or <see langword="null"/> where none is offered
    /// (<see cref="VpnHood.AppLib.Api.App.ConnectPlanId.Normal"/>).
    /// </summary>
    [JsonPropertyName("n")]
    public int? Normal { get; init; }

    /// <summary>
    /// Gets the minutes of an ordinary connection after a rewarded ad, 0 for no limit, or <see langword="null"/> where
    /// none is offered (<see cref="VpnHood.AppLib.Api.App.ConnectPlanId.NormalByRewardedAd"/>).
    /// </summary>
    [JsonPropertyName("nbr")]
    public int? NormalByRewardedAd { get; init; }

    /// <summary>
    /// Gets the minutes of a premium trial, 0 for no limit, or <see langword="null"/> where none is offered
    /// (<see cref="VpnHood.AppLib.Api.App.ConnectPlanId.PremiumByTrial"/>).
    /// </summary>
    [JsonPropertyName("pbt")]
    public int? PremiumByTrial { get; init; }

    /// <summary>Gets how much premium trial a day allows, or <see langword="null"/> for no daily limit.</summary>
    [JsonPropertyName("pbtdl")]
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? PremiumByTrialDailyLimit { get; init; }

    /// <summary>
    /// Gets the minutes of premium a rewarded ad gives, 0 for no limit, or <see langword="null"/> where none is offered
    /// (<see cref="VpnHood.AppLib.Api.App.ConnectPlanId.PremiumByRewardedAd"/>).
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

    /// <summary>Gets the shop this operator sells through, or <see langword="null"/>.</summary>
    /// <remarks>
    /// A shop named here takes the place of the app's own store: the app shows its page and no other, so an in-app store
    /// and an outside shop are never offered together. A build whose
    /// <see cref="Premium.AppPremiumOptions.IsPurchaseUrlSupported"/> is <see langword="false"/> ignores it.
    /// </remarks>
    [JsonPropertyName("pur_url")]
    public Uri? PurchaseUrl { get; init; }
}

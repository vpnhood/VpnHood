using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Tokens;

/// <summary>
/// The plan a client requests when starting a session. It tells the server how the user
/// is entitled to connect (free, ad-supported, trial, or premium).
/// See docs/ConnectionOptions.md for the product-level overview.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConnectPlanId>))]
public enum ConnectPlanId
{
    /// <summary>
    /// A standard free connection to a free (public) server, with no ad and no premium.
    /// This is the default plan.
    /// </summary>
    Normal,

    /// <summary>
    /// A free connection unlocked by watching a rewarded ad. Behaves like <see cref="Normal"/>,
    /// but the user watches a rewarded ad first, usually in exchange for a longer free session.
    /// Offered only when the policy enables it and the app can show rewarded ads.
    /// </summary>
    NormalByRewardedAd,

    /// <summary>
    /// A temporary premium connection granted as a free trial — no payment and no ad.
    /// </summary>
    PremiumByTrial,

    /// <summary>
    /// A temporary premium connection unlocked by watching a rewarded ad.
    /// </summary>
    PremiumByRewardedAd,

    /// <summary>
    /// Not an actual connection plan: a request used to query or refresh the current
    /// session status.
    /// </summary>
    Status
}

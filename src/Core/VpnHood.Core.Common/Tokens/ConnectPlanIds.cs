using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Tokens;

/// <summary>
/// The plan a client requests when starting a session. It tells the server how the user
/// is entitled to connect (free, ad-supported, trial, or premium).
/// See docs/connection-options.md for the product-level overview.
/// </summary>
// A name this side does not know reads as Normal: the plan with no ad and no premium.
[JsonConverter(typeof(JsonStringEnumFallbackConverter<ConnectPlanId>))]
[JsonEnumFallback(ConnectPlanId.Normal)]
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

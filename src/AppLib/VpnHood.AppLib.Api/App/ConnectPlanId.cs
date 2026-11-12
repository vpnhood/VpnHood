using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

/// <summary>
/// Specifies what the person asks for when they connect: a plain connection, one unlocked by watching an ad, a trial of
/// premium, or only the server's status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConnectPlanId>))]
public enum ConnectPlanId
{
    /// <summary>
    /// An ordinary connection, on what the access key and the server's policy give: premium where the key has it, and an
    /// ad where the server asks for one. This is the default.
    /// </summary>
    Normal,

    /// <summary>
    /// An ordinary connection the person pays for by watching a rewarded ad first, for a longer session where the policy
    /// gives one; offered only where the policy allows it and the app can show rewarded ads.
    /// </summary>
    NormalByRewardedAd,

    /// <summary>A premium trial, for as long as the server's policy gives, with no payment and no ad.</summary>
    PremiumByTrial,

    /// <summary>Premium for as long as the server's policy gives, which the person pays for by watching a rewarded ad.</summary>
    PremiumByRewardedAd,

    /// <summary>Reserved for a request of the server's status alone; the app does not use it, and how a server answers it is the server's.</summary>
    Status
}

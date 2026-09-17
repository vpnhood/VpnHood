using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.App;

// What the person is asking for when they connect: a plain connection, one unlocked by watching an
// ad, a trial of premium, or a status-only probe.
[JsonConverter(typeof(JsonStringEnumConverter<ConnectPlanId>))]
public enum ConnectPlanId
{
    Normal,
    NormalByRewardedAd,
    PremiumByTrial,
    PremiumByRewardedAd,
    Status
}

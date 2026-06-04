using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Tokens;

[JsonConverter(typeof(JsonStringEnumConverter<ConnectPlanId>))]
public enum ConnectPlanId
{
    Normal,
    NormalByRewardedAd,
    PremiumByTrial,
    PremiumByRewardedAd,
    Status
}

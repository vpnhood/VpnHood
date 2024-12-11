using System.Text.Json.Serialization;

namespace VpnHood.Common.Tokens;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConnectPlanId
{
    Normal,
    PremiumByTrial,
    PremiumByRewardedAd,
    Status
}
using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Tokens;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConnectPlanId
{
    Normal,
    PremiumByTrial,
    PremiumByRewardedAd,
    Status
}
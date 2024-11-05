using System.Text.Json.Serialization;

namespace VpnHood.Common.Tokens;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConnectPlanId
{
    Normal = 0,
    PremiumByTrial = 1,
    PremiumByAdReward = 2,
}
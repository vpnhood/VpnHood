using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class AdRewardRequest()
    : ClientRequest((byte)Messaging.RequestCode.RewardAd)
{
    public string? AdData { get; init; }
}
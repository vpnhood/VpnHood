namespace VpnHood.Tunneling.Messaging;

public class AdRewardRequest()
    : RequestBase(Messaging.RequestCode.AdReward)
{
    public required string AdData { get; init; }
}
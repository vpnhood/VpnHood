namespace VpnHood.Tunneling.Messaging;
public class AdRewardRequest()
    : RequestBase(Messaging.RequestCode.AdReward)
{
    public string? AdData { get; init; }
}

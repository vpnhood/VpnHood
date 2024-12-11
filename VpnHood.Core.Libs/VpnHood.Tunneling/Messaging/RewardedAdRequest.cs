namespace VpnHood.Tunneling.Messaging;

public class RewardedAdRequest()
    : RequestBase(Messaging.RequestCode.RewardedAd)
{
    public required string AdData { get; init; }
}
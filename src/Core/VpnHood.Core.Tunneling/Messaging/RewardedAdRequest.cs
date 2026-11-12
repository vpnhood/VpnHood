namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>
/// Represents a session's report of a rewarded ad the person has watched, which the server's access manager verifies.
/// </summary>
public class RewardedAdRequest()
    : RequestBase(Messaging.RequestCode.RewardedAd)
{
    /// <summary>Gets the ad's data, which the server passes to its access manager.</summary>
    public required string AdData { get; init; }
}
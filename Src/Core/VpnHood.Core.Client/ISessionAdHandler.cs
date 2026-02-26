namespace VpnHood.Core.Client;

public interface ISessionAdHandler
{
    Task WaitForAd(CancellationToken cancellationToken);
    void SetAdOk();
    Task SendRewardedAdData(string rewardedAdData, CancellationToken cancellationToken);
    void SetAdFailed(Exception ex);
}
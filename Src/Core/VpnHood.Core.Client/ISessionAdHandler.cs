namespace VpnHood.Core.Client;

public interface ISessionAdHandler : IDisposable
{
    event EventHandler? IsWaitingForChanged;
    bool IsWaitingForAd { get; }
    Task<Exception?> TryWaitForAd(CancellationToken cancellationToken);
    Task WaitForAd(CancellationToken cancellationToken);
    void SetAdOk();
    Task SendRewardedAdData(string rewardedAdData, CancellationToken cancellationToken);
    void SetAdFailed(Exception ex);
}
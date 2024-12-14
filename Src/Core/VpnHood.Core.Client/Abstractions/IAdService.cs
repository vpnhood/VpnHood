using VpnHood.Core.Client.Device;

namespace VpnHood.Core.Client.Abstractions;

public interface IAdService
{
    Task<ShowedAdResult> ShowInterstitial(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
    Task<ShowedAdResult> ShowRewarded(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
    bool CanShowRewarded { get; }
}
using VpnHood.Core.Client.Device;

namespace VpnHood.Core.Client.Abstractions;

public interface IAdService
{
    Task<ShowAdResult> ShowInterstitial(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
    Task<ShowAdResult> ShowRewarded(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
    bool CanShowRewarded { get; }
}
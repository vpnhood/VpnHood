using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;

namespace VpnHood.Core.Client;

public interface IAdService
{
    Task<ShowAdResult> ShowInterstitial(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
    Task<ShowAdResult> ShowRewarded(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
    bool CanShowRewarded { get; }
}
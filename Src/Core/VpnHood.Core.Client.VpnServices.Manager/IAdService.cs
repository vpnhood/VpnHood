using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.Core.Client.VpnServices.Manager;

public interface IAdService
{
    Task<AdResult> ShowInterstitial(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
    Task<AdResult> ShowRewarded(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
}
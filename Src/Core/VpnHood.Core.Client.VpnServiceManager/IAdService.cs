using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;

namespace VpnHood.Core.Client.VpnServiceManagement;

public interface IAdService
{
    Task<AdResult> ShowInterstitial(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
    Task<AdResult> ShowRewarded(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
}
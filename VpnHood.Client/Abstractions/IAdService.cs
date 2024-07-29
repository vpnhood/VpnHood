using VpnHood.Client.Device;

namespace VpnHood.Client.Abstractions;

public interface IAdService
{
    Task<ShowedAdResult> ShowAd(IUiContext uiContext, string sessionId, CancellationToken cancellationToken);
}
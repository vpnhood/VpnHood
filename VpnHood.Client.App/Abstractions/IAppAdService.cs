using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppAdService : IDisposable
{
    Task ShowAd(IUiContext appUiContext, string customData, CancellationToken cancellationToken);
}
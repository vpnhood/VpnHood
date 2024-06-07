using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppAdService : IDisposable
{
    AppAdNetworks NetworkName { get; }
    AppAdType AdType { get; }
    Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken);
    Task ShowAd(IUiContext uiContext, CancellationToken cancellationToken, string? customData, bool? isTestMode = false);
}
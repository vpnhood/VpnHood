using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppAdService : IDisposable
{
    string NetworkName { get; }
    AppAdType AdType { get; }
    Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken);
    Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken);
    bool IsCountrySupported(string countryCode);
    DateTime? AdLoadedTime { get; }
    TimeSpan AdLifeSpan { get; }
}
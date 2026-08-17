using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions.Ads;

public interface IAdProvider : IDisposable
{
    string NetworkName { get; }
    AdType AdType { get; }
    Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken);
    Task<ShowAdResult> ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken);
    DateTime? AdLoadedTime { get; }
    TimeSpan AdLifeSpan { get; }
}
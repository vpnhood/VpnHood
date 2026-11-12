using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Ads;

/// <summary>Represents one kind of ad of one ad network: it loads an ad, then shows it.</summary>
public interface IAdProvider : IDisposable
{
    /// <summary>Gets the ad network's name.</summary>
    string NetworkName { get; }

    /// <summary>Gets the kind of ad the provider shows.</summary>
    AdType AdType { get; }

    /// <summary>Loads an ad, to show it later.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once an ad is loaded.</returns>
    /// <exception cref="AdExceptions.NoFillAdException">The network had no ad to fill the request.</exception>
    /// <exception cref="AdExceptions.LoadAdException">
    /// No ad could be loaded, as where the consent the network needs is missing.
    /// </exception>
    /// <exception cref="AdExceptions.AdException">The network's SDK could not start, as AdMob's with no ad adapter.</exception>
    /// <exception cref="NotSupportedException">The network's SDK does not run on this Android version.</exception>
    /// <exception cref="TimeoutException">The network's SDK did not start in time.</exception>
    /// <exception cref="InvalidOperationException">The network's SDK did not start its initialization.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken);

    /// <summary>Shows the ad loaded.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="customData">
    /// The data the ad network passes to the server's check of a rewarded ad, or <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is how the ad ended.</returns>
    /// <exception cref="AdExceptions.ShowAdException">The ad could not be shown.</exception>
    /// <exception cref="AdExceptions.RewardNotEarnedException">
    /// For a rewarded ad, the network did not confirm the reward in time after the ad closed.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<ShowAdResult> ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken);

    /// <summary>Gets when the ad loaded now was loaded, in UTC, or <see langword="null"/> where none is.</summary>
    DateTime? AdLoadedTime { get; }

    /// <summary>Gets how long a loaded ad may wait before it is shown.</summary>
    TimeSpan AdLifeSpan { get; }
}
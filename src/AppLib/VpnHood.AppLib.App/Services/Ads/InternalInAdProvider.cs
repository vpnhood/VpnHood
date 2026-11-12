using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Services.Ads;

/// <summary>
/// Provides the app's own interstitial, which the UI shows and ends: the ad waits until the UI dismisses it
/// (<see cref="Dismiss"/>) or fails it (<see cref="SetException"/>).
/// </summary>
public class InternalInAdProvider : IAdProvider
{
    /// <inheritdoc/>
    public DateTime? AdLoadedTime { get; private set; }

    /// <inheritdoc/>
    /// <value>Five minutes.</value>
    public TimeSpan AdLifeSpan => TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    /// <value><c>InternalAd</c>.</value>
    public string NetworkName => "InternalAd";

    /// <inheritdoc/>
    /// <value><see cref="AdType.InterstitialAd"/>.</value>
    public AdType AdType => AdType.InterstitialAd;

    /// <summary>Gets a value indicating whether an ad is showing and waits for the UI to end it.</summary>
    public bool IsWaitingForAd => _showAdTask?.Task.IsCompleted is false;

    private TaskCompletionSource<ShowAdResult>? _showAdTask;

    /// <inheritdoc/>
    /// <remarks>It loads nothing and takes no cancel: it notes the time and returns at once.</remarks>
    public Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        AdLoadedTime = FastDateTime.UtcNow;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The task ends with the UI's answer (<see cref="Dismiss"/>), with the exception the UI gives
    /// (<see cref="SetException"/>), or canceled, when <paramref name="cancellationToken"/> is canceled or the provider
    /// is disposed.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the provider was disposed.
    /// </exception>
    /// <exception cref="Exception">The UI failed the ad with this exception (<see cref="SetException"/>).</exception>
    public Task<ShowAdResult> ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        _showAdTask = new TaskCompletionSource<ShowAdResult>();
        return _showAdTask.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Fails the ad showing, if one is.</summary>
    /// <param name="ex">The exception the ad's task ends with.</param>
    public void SetException(Exception ex)
    {
        _showAdTask?.TrySetException(ex);
    }

    /// <summary>Ends the ad showing, if one is.</summary>
    /// <param name="result">How the person ended it.</param>
    public void Dismiss(ShowAdResult result)
    {
        _showAdTask?.TrySetResult(result);
    }

    /// <inheritdoc/>
    /// <remarks>An ad showing is canceled.</remarks>
    public void Dispose()
    {
        _showAdTask?.TrySetCanceled();
    }
}
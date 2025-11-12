using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Ads;

public class InternalInAdProvider : IAppAdProvider
{
    public DateTime? AdLoadedTime { get; private set; }
    public TimeSpan AdLifeSpan => TimeSpan.FromMinutes(5);
    public string NetworkName => "InternalAd";
    public AppAdType AdType => AppAdType.InterstitialAd;
    public bool IsWaitingForAd => _showAdTask?.Task.IsCompleted is false;

    private TaskCompletionSource<ShowAdResult>? _showAdTask;

    public Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        AdLoadedTime = FastDateTime.Now;
        return Task.CompletedTask;
    }

    public Task<ShowAdResult> ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        _showAdTask = new TaskCompletionSource<ShowAdResult>();
        return _showAdTask.Task.WaitAsync(cancellationToken);
    }

    public void SetException(Exception ex)
    {
        _showAdTask?.TrySetException(ex);
    }

    public void Dismiss(ShowAdResult result)
    {
        _showAdTask?.TrySetResult(result);
    }

    public void Dispose()
    {
        _showAdTask?.TrySetCanceled();
    }
}
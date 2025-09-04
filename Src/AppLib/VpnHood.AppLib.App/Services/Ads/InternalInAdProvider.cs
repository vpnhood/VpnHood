using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.AdExceptions;
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

    private TaskCompletionSource? _showAdTask;
    public Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        AdLoadedTime = FastDateTime.Now;
        return Task.CompletedTask;
    }

    public Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        _showAdTask = new TaskCompletionSource();
        return _showAdTask.Task.WaitAsync(cancellationToken);   
    }

    public void Dismiss()
    {
        _showAdTask?.TrySetResult();
    }

    public void Dispose()
    {
        _showAdTask?.TrySetCanceled();
    }

}
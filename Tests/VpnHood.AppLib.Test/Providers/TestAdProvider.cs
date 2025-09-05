using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.AdExceptions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Test.AccessManagers;

namespace VpnHood.AppLib.Test.Providers;

public class TestAdProvider(TestAccessManager accessManager, AppAdType adType = AppAdType.RewardedAd) 
    : IAppAdProvider
{
    public bool FailShow { get; set; }
    public bool FailLoad { get; set; }
    public string NetworkName => "UnitTestNetwork";
    public AppAdType AdType => adType;
    public DateTime? AdLoadedTime { get; private set; }
    public TimeSpan AdLifeSpan { get; } = TimeSpan.FromMinutes(60);
    public TaskCompletionSource<ShowAdResult>? ShowAdCompletionSource { get; set; }
    public TaskCompletionSource? LoadAdCompletionSource { get; set; }
    public int LoadAdCount { get; private set; }
    public Func<Task> LoadAdCallback { get; set; } = () => Task.CompletedTask;

    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        LoadAdCount++;
        AdLoadedTime = null;
        if (FailLoad)
            throw new LoadAdException("Load Ad failed by test.");

        await LoadAdCallback.Invoke();

        AdLoadedTime = DateTime.Now;
        if (LoadAdCompletionSource != null)
            await LoadAdCompletionSource.Task;
    }

    public Task<ShowAdResult> ShowAd(IUiContext uiContext, 
        string? customData, 
        CancellationToken cancellationToken)
    {
        if (AdLoadedTime == null)
            throw new ShowAdException("Not Ad has been loaded.");

        try {
            if (FailShow)
                throw new ShowAdException("Show Ad failed by test.");

            if (!string.IsNullOrEmpty(customData))
                accessManager.AddAdData(customData);

            return ShowAdCompletionSource?.Task ?? Task.FromResult(ShowAdResult.Closed);
        }
        finally {
            AdLoadedTime = null;
        }
    }

    public void Dispose()
    {
    }
}
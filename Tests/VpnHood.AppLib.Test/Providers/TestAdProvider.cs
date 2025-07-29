using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Test.AccessManagers;

namespace VpnHood.AppLib.Test.Providers;

public class TestAdProvider(TestAccessManager accessManager, AppAdType adType = AppAdType.RewardedAd) : IAppAdProvider
{
    public bool FailShow { get; set; }
    public bool FailLoad { get; set; }
    public string NetworkName => "UnitTestNetwork";
    public AppAdType AdType => adType;
    public DateTime? AdLoadedTime { get; private set; }
    public TimeSpan AdLifeSpan { get; } = TimeSpan.FromMinutes(60);
    public TaskCompletionSource? ShowAdCompletionSource { get; set; }

    public Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        AdLoadedTime = null;
        if (FailLoad)
            throw new LoadAdException("Load Ad failed.");

        AdLoadedTime = DateTime.Now;
        return Task.CompletedTask;
    }

    public Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        if (AdLoadedTime == null)
            throw new ShowAdException("Not Ad has been loaded.");

        try {
            if (FailShow)
                throw new ShowAdException("Ad failed.");

            if (!string.IsNullOrEmpty(customData))
                accessManager.AddAdData(customData);

            return ShowAdCompletionSource?.Task ?? Task.CompletedTask;
        }
        finally {
            AdLoadedTime = null;
        }
    }

    public void Dispose()
    {
    }
}
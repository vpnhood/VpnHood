using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.Exceptions;
using VpnHood.Test.AccessManagers;

namespace VpnHood.Test.Providers;

public class TestAdProvider(TestAccessManager accessManager) : IAppAdProvider
{
    public bool FailShow { get; set; }
    public bool FailLoad { get; set; }
    public string NetworkName => "";
    public AppAdType AdType => AppAdType.InterstitialAd;
    public DateTime? AdLoadedTime { get; private set; }
    public TimeSpan AdLifeSpan { get; } = TimeSpan.FromMinutes(60);

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

            return Task.CompletedTask;
        }
        finally {
            AdLoadedTime = null;
        }
    }

    public void Dispose()
    {
    }
}
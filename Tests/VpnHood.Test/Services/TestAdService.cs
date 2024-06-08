using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Client.Exceptions;
using VpnHood.Test.AccessManagers;

namespace VpnHood.Test.Services;

public class TestAdService(TestAccessManager accessManager) : IAppAdService
{
    public bool FailShow { get; set; }
    public bool FailLoad { get; set; }
    public string NetworkName => "";
    public AppAdType AdType => AppAdType.InterstitialAd;

    public Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        if (FailLoad)
            throw new AdLoadException("Load Ad failed.");

        if (FailShow)
            throw new Exception("Ad failed.");

        accessManager.AddAdData(customData ??
                                throw new AdException("The custom data for the rewarded ads is required."));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Client.Exceptions;
using VpnHood.Test.AccessManagers;

namespace VpnHood.Test.Services;

public class TestAdService(TestAccessManager accessManager) : IAppAdService
{
    private bool _isAddLoaded;
    public bool FailShow { get; set; }
    public bool FailLoad { get; set; }
    public string NetworkName => "";
    public AppAdType AdType => AppAdType.InterstitialAd;

    public Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        if (FailLoad)
            throw new AdLoadException("Load Ad failed.");

        _isAddLoaded = true;
        return Task.CompletedTask;
    }

    public Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        if (!_isAddLoaded)
            throw new AdLoadException("Not Ad has been loaded.");

        if (FailShow)
            throw new Exception("Ad failed.");

        if (!string.IsNullOrEmpty(customData))
            accessManager.AddAdData(customData);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
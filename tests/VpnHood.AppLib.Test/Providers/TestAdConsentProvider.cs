using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

// An ad network's consent form that the person reads for ReadingTime.
public class TestAdConsentProvider : IAdConsentProvider
{
    public TimeSpan ReadingTime { get; set; }
    public bool IsGathered { get; private set; }

    public async Task GatherConsent(IUiContext uiContext, CancellationToken cancellationToken)
    {
        await Task.Delay(ReadingTime, cancellationToken);
        IsGathered = true;
    }

    public Task<bool> CanRequestAds(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(IsGathered);
    }

    public bool IsPrivacyOptionsRequired => IsGathered;

    public Task ShowPrivacyOptions(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.Ads.AdMob.Android;

// AdMob's consent for the app (AppAdOptions.ConsentProvider): its form before an ad's timed load,
// whether a preload may go ahead without the form, and the privacy settings' way back to the answer;
// AdMob's ads also ask for it by themselves (AdMobConsent).
public class AdMobConsentProvider : IAdConsentProvider
{
    public Task GatherConsent(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var activity = ((AndroidUiContext)uiContext).Activity;
        return AdMobConsent.TryGather(activity, cancellationToken);
    }

    public async Task<bool> CanRequestAds(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var activity = ((AndroidUiContext)uiContext).Activity;
        try {
            await AdMobConsent.Update(activity, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            // Google's rule: an answer from an earlier run still counts when this one cannot get one
            VhLogger.Instance.LogWarning(ex, "Could not update the ad consent.");
        }

        return AdMobConsent.CanRequestAds;
    }

    public bool IsPrivacyOptionsRequired => AdMobConsent.IsPrivacyOptionsRequired;

    public Task ShowPrivacyOptions(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var activity = ((AndroidUiContext)uiContext).Activity;
        return AdMobConsent.ShowPrivacyOptions(activity, cancellationToken);
    }
}

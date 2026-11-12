using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.Ads.AdMob.Android;

/// <summary>
/// Provides AdMob's consent, through Google's User Messaging Platform: its form before an ad's timed load, whether an ad
/// may be preloaded without the form, and the privacy settings' way back to the answer.
/// </summary>
/// <remarks>AdMob's ads also gather the consent by themselves before they load.</remarks>
public class AdMobConsentProvider : IAdConsentProvider
{
    /// <inheritdoc/>
    /// <remarks>
    /// A failure is logged: by Google's rule, an answer from an earlier run still counts when this one cannot get one.
    /// The gathering is shared by its callers: a cancel ends the caller's wait, not the form.
    /// </remarks>
    public Task GatherConsent(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var activity = ((AndroidUiContext)uiContext).Activity;
        return AdMobConsent.TryGather(activity, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It makes sure of the consent information's first update, which it tries again after a failure, logging it, and
    /// reads the answer as it stands.
    /// </remarks>
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

    /// <inheritdoc/>
    /// <remarks>It is <see langword="true"/> where the person answered a form Google requires a way back to.</remarks>
    public bool IsPrivacyOptionsRequired => AdMobConsent.IsPrivacyOptionsRequired;

    /// <inheritdoc/>
    public Task ShowPrivacyOptions(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var activity = ((AndroidUiContext)uiContext).Activity;
        return AdMobConsent.ShowPrivacyOptions(activity, cancellationToken);
    }
}

using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Ads;

// An ad network's consent, where a law wants a yes before ads use personal data. The app asks for it
// before an ad's timed load, so the person reads the form for as long as they need; it also tells
// whether an ad may be preloaded without the form, and is the way back to the answer, for the app's
// privacy settings.
public interface IAdConsentProvider
{
    // the network's form, where one is due and not answered yet; a failure is left to the ad's load,
    // which refuses an ad without the answer
    Task GatherConsent(IUiContext uiContext, CancellationToken cancellationToken);

    // the person has answered, or no answer is due where they are, as the network says now
    Task<bool> CanRequestAds(IUiContext uiContext, CancellationToken cancellationToken);

    // the person was asked, so they must be able to change their answer
    bool IsPrivacyOptionsRequired { get; }
    Task ShowPrivacyOptions(IUiContext uiContext, CancellationToken cancellationToken);
}

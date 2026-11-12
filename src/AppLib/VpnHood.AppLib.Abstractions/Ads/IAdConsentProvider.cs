using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Ads;

/// <summary>
/// Represents an ad network's consent, where a law wants a yes before ads use personal data.
/// </summary>
/// <remarks>
/// The app asks for it before an ad's timed load, so the person reads the form for as long as they need; it also tells
/// whether an ad may be preloaded without the form, and is the way back to the answer, in the app's privacy settings.
/// </remarks>
public interface IAdConsentProvider
{
    /// <summary>Shows the network's form, where one is due and not answered yet.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that completes once the gathering has ended: the form answered, none due, or a failure.
    /// </returns>
    /// <remarks>A failure is left to the ad's load, which refuses an ad without the answer.</remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task GatherConsent(IUiContext uiContext, CancellationToken cancellationToken);

    /// <summary>Gets whether ads may be asked for now, as the network says.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> where the person has answered, or no answer is due where they are;
    /// where the network cannot be reached, its last answer stands.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> CanRequestAds(IUiContext uiContext, CancellationToken cancellationToken);

    /// <summary>Gets a value indicating whether the person was asked, so they must be able to change their answer.</summary>
    bool IsPrivacyOptionsRequired { get; }

    /// <summary>Shows the network's form again, for the person to change their answer.</summary>
    /// <param name="uiContext">The app's UI context.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the form is closed.</returns>
    /// <exception cref="AdExceptions.AdException">The network's form failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task ShowPrivacyOptions(IUiContext uiContext, CancellationToken cancellationToken);
}

using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// Represents an identity provider's own sign-in on the device, as Google's or Apple's, which gives the ID token the
/// account backend verifies.
/// </summary>
public interface IAuthenticationExternalProvider : IDisposable
{
    /// <summary>
    /// The identity provider this implementation drives, self-declared as a free-form string id
    /// (lowercase by convention; see <see cref="AuthProviders"/> for the well-known ones) so nothing downstream
    /// ever hardcodes it: the portal sends it verbatim as the wire discriminator, AppDescriptor reports
    /// it, and the UI picks the matching "Sign in with X" label. A string rather than an enum so a
    /// third-party app on these packages can add its own provider — no library change, no consumer
    /// changes.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>Signs the person in with the identity provider.</summary>
    /// <param name="uiContext">The window, or <see langword="null"/> where none is open.</param>
    /// <param name="isSilentLogin">
    /// <see langword="true"/> where the person did not ask to sign in, as for a renewal, so the provider avoids a prompt
    /// where it can.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the identity provider's ID token.</returns>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the provider shows a sheet.
    /// </exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">
    /// The identity provider gave no ID token, as Google's where the device has no Google account.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="isSilentLogin"/> is <see langword="true"/> where the provider cannot sign in without the person,
    /// as Apple's while the app is not in front.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The person canceled the sign-in, or <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    public Task<string> SignIn(IUiContext? uiContext, bool isSilentLogin, CancellationToken cancellationToken);

    /// <summary>Clears the identity provider's sign-in on this device, where it keeps one.</summary>
    /// <param name="uiContext">The window, or <see langword="null"/> where none is open.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the provider needs a window to clear it.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The platform failed to clear it, as Google's credential manager reports a failure or a cancel.
    /// </exception>
    public Task SignOut(IUiContext? uiContext, CancellationToken cancellationToken);
}
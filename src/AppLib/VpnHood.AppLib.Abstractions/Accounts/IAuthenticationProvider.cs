using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// Who is signed in, and the credential that proves it. Deliberately not a transport: it hands out
/// a token and lets the caller own its own HttpClient, so nothing downstream inherits a client's
/// base address, TLS policy or lifetime from here.
/// <para>
/// Disposable because it owns the external providers it was given, not because it owns any client.
/// </para>
/// </summary>
public interface IAuthenticationProvider : IDisposable
{
    /// <summary>
    /// Gets the IDs of the sign-in methods the app offers, the ones <see cref="SignInOptions.ProviderId"/> may name; the
    /// well-known ones are in <see cref="AuthProviders"/>.
    /// </summary>
    IReadOnlyList<string> ProviderIds { get; }

    /// <summary>
    /// The page of the human account website behind this provider where a person recovers a
    /// password, or null when there is none. Asked when the password sign-in form opens, which
    /// offers it as its escape hatch only when a page comes back — never as an API endpoint.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the page, or <see langword="null"/> where there is none.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">The account backend refused the request.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<Uri?> GetPasswordResetUrl(CancellationToken cancellationToken);

    /// <summary>
    /// The signed-in account's id, or null. Ambient and cheap: it is read straight from the
    /// persisted session, so a device signed in yesterday knows it without any call.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// The credential for the current session, or null when nobody is signed in; an anonymous call
    /// simply never asks for one. Async, and hence not a property beside <see cref="UserId" />, as a
    /// provider may have to work for it: the portal's tries a zero-tap restore of the sign-in where
    /// nobody is signed in. The portal never renews it: its sessions have no time limit and last
    /// until the backend refuses them (<see cref="InvalidateAccessToken" />).
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the credential, or <see langword="null"/> where nobody is signed in.</returns>
    Task<string?> GetAccessToken(CancellationToken cancellationToken);

    /// <summary>
    /// The backend refused this token: the session is over. The token is passed back so a stale
    /// in-flight refusal cannot end a session that has meanwhile been replaced by a newer sign-in.
    /// </summary>
    /// <param name="accessToken">The token the backend refused.</param>
    void InvalidateAccessToken(string accessToken);

    /// <summary>Signs the person in, or answers the second step of a sign-in that asked for one.</summary>
    /// <param name="uiContext">
    /// The window, or <see langword="null"/> where none is open, as in the desktop service: the password sign-in needs
    /// none.
    /// </param>
    /// <param name="signInOptions">The sign-in method, with its credentials or the second step's code.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is what the sign-in accomplished.</returns>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the method shows a sheet, as Google's and Apple's do.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The method is not one of <see cref="ProviderIds"/>, or the account's second step is of a kind the app cannot
    /// answer.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The password sign-in's first step lacks the email address or the password.
    /// </exception>
    /// <exception cref="InvalidOperationException">A second step's code came with no sign-in waiting for one.</exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">
    /// The identity provider gave no ID token, as Google's where the device has no Google account.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">The account backend refused the request.</exception>
    /// <exception cref="OperationCanceledException">
    /// The person canceled the sign-in, or <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    Task<SignInResult> SignIn(IUiContext? uiContext, SignInOptions signInOptions, CancellationToken cancellationToken);

    /// <summary>Signs the person out.</summary>
    /// <param name="uiContext">The window, or <see langword="null"/> where none is open.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// The session ends on the device either way; what only a window can clear on the device is left where none is
    /// open.
    /// </remarks>
    Task SignOut(IUiContext? uiContext, CancellationToken cancellationToken);
}

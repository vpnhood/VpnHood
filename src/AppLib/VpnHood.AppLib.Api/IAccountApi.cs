using VpnHood.AppLib.Api.Accounts;

namespace VpnHood.AppLib.Api;

/// <summary>Represents the app's API for the person's account: the sign-in, the account, and its deletion.</summary>
/// <remarks>
/// Where the app has no account backend, <see cref="Get"/> and <see cref="GetPasswordResetUrl"/> return
/// <see langword="null"/>, and the other calls fail. A failure comes as <see cref="IAppApi"/> describes, in process and
/// over HTTP.
/// </remarks>
public interface IAccountApi
{
    /// <summary>
    /// Signs the person in, or answers a sign-in's second step with <see cref="SignInOptions.TwoFactorCode"/>.
    /// </summary>
    /// <param name="signInOptions">The sign-in method, with its credentials or the second step's code.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is what the sign-in accomplished.</returns>
    /// <exception cref="System.Security.Authentication.AuthenticationException">The account backend refused the sign-in.</exception>
    /// <exception cref="InvalidOperationException">
    /// The provider cannot sign in now, or the account's refresh after it found no current VPN profile, or several and none
    /// selected.
    /// </exception>
    /// <exception cref="ArgumentException">The sign-in options do not fit the method.</exception>
    /// <exception cref="NotSupportedException">
    /// The app offers no such sign-in method, or the method's provider does not take the request.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The account backend refused the request; or, over HTTP, the app failed with an exception of a type the transport
    /// does not rebuild, or answered with an error it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The person canceled the sign-in; or <paramref name="cancellationToken"/> was canceled.
    /// </exception>
    /// <remarks>
    /// A result whose <see cref="SignInResult.State"/> is not <see cref="SignInState.SignedIn"/> signed nobody in: the call
    /// is repeated with the code. A <see cref="SignInResult.NewBackupCode"/> is shown to the person once. A method that
    /// shows a window needs the app's, and fails without it, as the app's <c>UiContextNotAvailableException</c>.
    /// </remarks>
    Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken);

    /// <summary>Signs the person out.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">
    /// A premium code waits to be uploaded, and the account backend keeps no codes on the account.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The account's refresh found no current VPN profile, or several and none selected.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The account backend refused the request; or, over HTTP, the app failed with an exception of a type the transport
    /// does not rebuild, or answered with an error it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task SignOut(CancellationToken cancellationToken);

    /// <summary>Deletes the person's account, and signs the device out.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the account is deleted and the device signed out.</returns>
    /// <exception cref="NotSupportedException">
    /// A premium code waits to be uploaded, and the account backend keeps no codes on the account.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The account's refresh found no current VPN profile, or several and none selected.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The account backend refused the request; or, over HTTP, the app failed with an exception of a type the transport
    /// does not rebuild, or answered with an error it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>
    /// A store subscription stays: only the person can cancel it, in the store. A later sign-in tries to restore it, and
    /// the Restore action tries again.
    /// </remarks>
    Task Delete(CancellationToken cancellationToken);

    /// <summary>Reads the account again from the account backend, with its premium code and subscription.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">
    /// A premium code waits to be uploaded, and the account backend keeps no codes on the account.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The account's refresh found no current VPN profile, or several and none selected.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The account backend refused the request; or, over HTTP, the app failed with an exception of a type the transport
    /// does not rebuild, or answered with an error it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task Refresh(CancellationToken cancellationToken);

    /// <summary>Gets the signed-in person's account.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the account, or <see langword="null"/> where nobody is signed in or the app has no account
    /// backend.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// A premium code waits to be uploaded, and the account backend keeps no codes on the account.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The account's refresh found no current VPN profile, or several and none selected.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The account backend refused the request; or, over HTTP, the app failed with an exception of a type the transport
    /// does not rebuild, or answered with an error it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    /// <remarks>
    /// It reads the account backend where nothing is cached, or where the cached account has expired by its subscription's
    /// or its premium code's expiry, unless an earlier read is still within the retry interval; then it gives the cached
    /// account.
    /// </remarks>
    Task<Account?> Get(CancellationToken cancellationToken);

    /// <summary>Gets the account website's page for a forgotten password, as the password form asks when it opens.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the page, or <see langword="null"/> where the app has no account backend or the backend has
    /// no such page; the form offers its link only with a page.
    /// </returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, which
    /// <see cref="VpnHood.Net.Toolkit.ApiClients.ApiException.ExceptionTypeName"/> names, or answered with an error
    /// it does not name.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled: over HTTP always, in process where the call honours it.
    /// </exception>
    Task<Uri?> GetPasswordResetUrl(CancellationToken cancellationToken);
}

using System.Security.Authentication;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using Xamarin.GoogleAndroid.Libraries.Identity.GoogleId;
using VpnHood.AppLib.Abstractions.UiContexts;

namespace VpnHood.AppLib.Stores.GooglePlay;

/// <summary>Provides the sign-in with Google, through Android's Credential Manager, which gives Google's ID token.</summary>
/// <param name="googleSignInClientId">The server's OAuth client ID, which Google issues the ID token for.</param>
public class GooglePlayAuthenticationProvider(string googleSignInClientId) : IAuthenticationExternalProvider
{
    /// <inheritdoc/>
    /// <remarks>It is <see cref="AuthProviders.Google"/>.</remarks>
    public string ProviderId => AuthProviders.Google;

    /// <inheritdoc/>
    /// <remarks>
    /// It first asks for a Google ID credential, which is picked without the person where
    /// <paramref name="isSilentLogin"/> is <see langword="true"/> and the device allows it; where the device offers none
    /// and the sign-in is not silent, it shows the "Sign in with Google" sheet.
    /// </remarks>
    /// <exception cref="System.Security.Authentication.AuthenticationException">
    /// The device has no Google account, or Google's sign-in failed.
    /// </exception>
    /// <exception cref="AndroidX.Credentials.Exceptions.NoCredentialException">
    /// The sign-in is silent, and the device offers no credential.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">The Credential Manager failed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The activity has no main executor, or the Credential Manager gave no response, or one of another type.
    /// </exception>
    public async Task<string> SignIn(IUiContext? uiContext, bool isSilentLogin,
        CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)(uiContext ?? throw new UiContextNotAvailableException());
        using var partialActivityScope = AppUiContext.CreatePartialIntentScope();
        using var credentialManager = GoogleCredentialManager.Create(appUiContext.Activity);
        var nonce = Guid.NewGuid().ToString();

        // Try GetGoogleIdOption first (supports silent/auto-select login)
        try {
            using var googleIdOption = new GetGoogleIdOption.Builder()
                .SetFilterByAuthorizedAccounts(false)
                .SetServerClientId(googleSignInClientId)
                .SetAutoSelectEnabled(isSilentLogin)
                .SetNonce(nonce)
                .Build();

            using var credentialRequest = new GetCredentialRequest.Builder().AddCredentialOption(googleIdOption).Build();
            using var credentialResponse = await credentialManager
                .GetCredentialAsync(appUiContext.Activity, credentialRequest, cancellationToken)
                .ConfigureAwait(false);
            return GetIdTokenFromCredentialResponse(credentialResponse);
        }
        catch (NoCredentialException) when (!isSilentLogin) {
            // Use GetSignInWithGoogleOption as fallback to show the interactive sign-in UI
            return await SignInWithGoogle(appUiContext, credentialManager, nonce, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> SignInWithGoogle(AndroidUiContext appUiContext,
        GoogleCredentialManager credentialManager, string nonce, CancellationToken cancellationToken)
    {
        try {
            using var signInOption = new GetSignInWithGoogleOption.Builder(googleSignInClientId)
                .SetNonce(nonce)
                .Build();
            using var fallbackRequest =
                new GetCredentialRequest.Builder().AddCredentialOption(signInOption).Build();
            using var fallbackResponse = await credentialManager
                .GetCredentialAsync(appUiContext.Activity, fallbackRequest, cancellationToken)
                .ConfigureAwait(false);
            return GetIdTokenFromCredentialResponse(fallbackResponse);
        }
        catch (NoCredentialException ex) {
            // The sheet had nothing to offer because the device has no Google account at all.
            // Without this, the caller sees a cancellation and stays silent (cancelling is
            // deliberately not reported), so sign-in appears to do nothing on a fresh device.
            throw new AuthenticationException(
                "There is no Google account on this device. Add one in the device settings and try again.", ex);
        }
    }

    /// <inheritdoc/>
    /// <remarks>It clears the Credential Manager's sign-in state for the app.</remarks>
    /// <exception cref="InvalidOperationException">The activity has no main executor.</exception>
    public async Task SignOut(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)(uiContext ?? throw new UiContextNotAvailableException());
        using var credentialManager = GoogleCredentialManager.Create(appUiContext.Activity);
        await credentialManager.ClearCredentialStateAsync(appUiContext.Activity, cancellationToken).ConfigureAwait(false);
    }

    private static string GetIdTokenFromCredentialResponse(GetCredentialResponse credentialResponse)
    {
        var credential = credentialResponse.Credential;
        if (credential is not CustomCredential ||
            credential.Type is not GoogleIdTokenCredential.TypeGoogleIdTokenCredential)
            throw new AuthenticationException("Unexpected type of credential");

        using var googleIdTokenCredential = GoogleIdTokenCredential.CreateFrom(credential.Data);
        return googleIdTokenCredential.IdToken;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
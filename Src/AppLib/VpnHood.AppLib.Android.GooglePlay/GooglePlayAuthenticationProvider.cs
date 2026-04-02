using System.Security.Authentication;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Client.Device.UiContexts;
using Xamarin.GoogleAndroid.Libraries.Identity.GoogleId;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayAuthenticationProvider(string googleSignInClientId) : IAppAuthenticationExternalProvider
{
    public async Task<string> SignIn(IUiContext uiContext, bool isSilentLogin, 
        CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
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
        catch (AuthenticationException ex) when (ex.Message.Contains("CancellationException")) {
            // GetCredentialCancellationException from user dismissing the dialog
            throw new UserCanceledException(ex.Message, ex);
        }
    }

    public async Task SignOut(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
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

    public void Dispose()
    {
    }
}
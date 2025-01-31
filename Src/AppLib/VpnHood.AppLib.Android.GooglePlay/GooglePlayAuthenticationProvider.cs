using System.Security.Authentication;
using AndroidX.Credentials;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.Droid;
using Xamarin.GoogleAndroid.Libraries.Identity.GoogleId;
using GetCredentialRequest = AndroidX.Credentials.GetCredentialRequest;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayAuthenticationProvider(string googleSignInClientId) : IAppAuthenticationExternalProvider
{
    public async Task<string> SignIn(IUiContext uiContext, bool isSilentLogin)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var partialActivityScope = ActiveUiContext.CreatePartialIntentScope();

        // GetGoogleIdOption is used to specify the options for the Google
        using var googleSignInOptions = new GetGoogleIdOption.Builder()
                .SetFilterByAuthorizedAccounts(false)
                .SetServerClientId(googleSignInClientId)
                .SetAutoSelectEnabled(isSilentLogin)
                .Build();

        using var credentialRequest = new GetCredentialRequest.Builder().AddCredentialOption(googleSignInOptions).Build();
        using var credentialManager = GoogleCredentialManager.Create(appUiContext.Activity);
        using var credentialResponse = await credentialManager.GetCredentialAsync(appUiContext.Activity, credentialRequest).ConfigureAwait(false);
        return GetIdTokenFromCredentialResponse(credentialResponse);
    }
    
    public async Task SignOut(IUiContext uiContext)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var credentialManager = GoogleCredentialManager.Create(appUiContext.Activity);
        await credentialManager.ClearCredentialStateAsync(appUiContext.Activity).ConfigureAwait(false);
    }

    private static string GetIdTokenFromCredentialResponse(GetCredentialResponse credentialResponse)
    {
        var credential = credentialResponse.Credential;
        if (credential is not CustomCredential || credential.Type is not GoogleIdTokenCredential.TypeGoogleIdTokenCredential) 
            throw new AuthenticationException("Unexpected type of credential");
        
        using var googleIdTokenCredential = GoogleIdTokenCredential.CreateFrom(credential.Data);
        return googleIdTokenCredential.IdToken;
    }
    public void Dispose()
    {
    }
}
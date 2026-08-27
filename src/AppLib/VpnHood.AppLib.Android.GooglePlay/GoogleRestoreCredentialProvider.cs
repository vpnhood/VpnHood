using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.Droid;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Droid.GooglePlay;

/// <summary>
/// Android Restore Credentials (zero-tap sign-in restoration, a Play requirement for sign-in apps
/// from April 2027): a Credential Manager key pair that follows the person through device-to-device
/// transfer and cloud restore. Every options/response JSON passes through VERBATIM — the portal is
/// the relying party and owns the ceremony; this class only drives the platform API.
/// </summary>
public class GoogleRestoreCredentialProvider : IRestoreCredentialProvider
{
    public async Task<string> Create(IUiContext uiContext, string requestJson, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var credentialManager = GoogleCredentialManager.Create(appUiContext.Activity);
        using var request = new CreateRestoreCredentialRequest(requestJson);
        using var response = await credentialManager
            .CreateCredentialAsync(appUiContext.Activity, request, cancellationToken)
            .ConfigureAwait(false);

        if (response is not CreateRestoreCredentialResponse restoreResponse)
            throw new InvalidOperationException(
                $"Credential manager returned an unexpected response type: {response.Type}");
        return restoreResponse.ResponseJson;
    }

    public async Task<string?> TryGet(IUiContext uiContext, string requestJson, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var credentialManager = GoogleCredentialManager.Create(appUiContext.Activity);
        using var restoreOption = new GetRestoreCredentialOption(requestJson);
        using var credentialRequest = new GetCredentialRequest.Builder().AddCredentialOption(restoreOption).Build();

        try {
            using var credentialResponse = await credentialManager
                .GetCredentialAsync(appUiContext.Activity, credentialRequest, cancellationToken)
                .ConfigureAwait(false);

            if (credentialResponse.Credential is not RestoreCredential restoreCredential)
                throw new InvalidOperationException(
                    $"Credential manager returned an unexpected credential type: {credentialResponse.Credential.Type}");

            return restoreCredential.AuthenticationResponseJson;
        }
        catch (NoCredentialException) {
            // a fresh install that was never restored — the caller signs in interactively instead
            return null;
        }
    }

    public async Task Clear(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var credentialManager = GoogleCredentialManager.Create(appUiContext.Activity);
        using var request = new ClearCredentialStateRequest(ClearCredentialStateRequest.TypeClearRestoreCredential);
        await credentialManager.ClearCredentialStateAsync(appUiContext.Activity, request, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
    }
}

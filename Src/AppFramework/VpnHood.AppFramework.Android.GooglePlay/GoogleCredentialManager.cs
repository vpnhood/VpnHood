using AndroidX.Credentials;
using VpnHood.Common.Utils;

namespace VpnHood.AppFramework.Droid.GooglePlay;

public class GoogleCredentialManager(ICredentialManager credentialManager) : IDisposable
{
    public static GoogleCredentialManager Create(Activity activity)
    {
        var manager = ICredentialManager.Create(activity);
        return new GoogleCredentialManager(manager);
    }

    public async Task<GetCredentialResponse> GetCredentialAsync(Activity activity, GetCredentialRequest credentialRequest)
    {
        using var credentialManagerCallback = new CredentialManagerCallback();
        credentialManager.GetCredentialAsync(activity, credentialRequest, null, 
            activity.MainExecutor!, credentialManagerCallback);
        var credentialResponse = await credentialManagerCallback.GetResultAsync().VhConfigureAwait();
        return credentialResponse;
    }
    public async Task ClearCredentialStateAsync(Activity activity)
    {
        using var request = new ClearCredentialStateRequest();
        using var credentialManagerCallback = new CredentialManagerCallback();
        credentialManager.ClearCredentialStateAsync(request, null, 
            activity.MainExecutor!, credentialManagerCallback);
        await credentialManagerCallback.GetResultAsync().VhConfigureAwait();
    }

    public void Dispose()
    {
        credentialManager.Dispose();
    }
}
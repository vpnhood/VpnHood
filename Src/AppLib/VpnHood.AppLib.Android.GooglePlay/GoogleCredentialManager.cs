using AndroidX.Credentials;

namespace VpnHood.AppLib.Droid.GooglePlay;

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
        var credentialResponse = await credentialManagerCallback.GetResultAsync().ConfigureAwait(false);
        return credentialResponse;
    }
    public async Task ClearCredentialStateAsync(Activity activity)
    {
        using var request = new ClearCredentialStateRequest();
        using var credentialManagerCallback = new CredentialManagerCallback();
        credentialManager.ClearCredentialStateAsync(request, null, 
            activity.MainExecutor!, credentialManagerCallback);
        await credentialManagerCallback.GetResultAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        credentialManager.Dispose();
    }
}
using AndroidX.Credentials;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GoogleCredentialManager(ICredentialManager credentialManager) : IDisposable
{
    public static GoogleCredentialManager Create(Activity activity)
    {
        var manager = ICredentialManager.Create(activity);
        return new GoogleCredentialManager(manager);
    }

    public async Task<GetCredentialResponse> GetCredentialAsync(Activity activity,
        GetCredentialRequest credentialRequest, CancellationToken cancellationToken)
    {
        using var credentialManagerCallback = new CredentialManagerCallback();
        var cancellationSignal = cancellationToken.ToCancellationSignal(); // do not dispose this
        credentialManager.GetCredentialAsync(activity, credentialRequest, cancellationSignal,
            activity.MainExecutor!, credentialManagerCallback);
            var credentialResponse = await credentialManagerCallback.GetResultAsync().ConfigureAwait(false);
            return credentialResponse;
    }

    public async Task ClearCredentialStateAsync(Activity activity, CancellationToken cancellationToken)
    {
        using var request = new ClearCredentialStateRequest();
        using var credentialManagerCallback = new CredentialManagerCallback();
        var cancellationSignal = cancellationToken.ToCancellationSignal(); // do not dispose this
        credentialManager.ClearCredentialStateAsync(request, cancellationSignal,
            activity.MainExecutor!, credentialManagerCallback);
        await credentialManagerCallback.GetResultAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        credentialManager.Dispose();
    }
}
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
        var mainExecutor = activity.MainExecutor ?? throw new InvalidOperationException("Activity has no main executor.");
        using var credentialManagerCallback = new CredentialManagerCallback();
        var cancellationSignal = cancellationToken.ToCancellationSignal(); // do not dispose this
        credentialManager.GetCredentialAsync(activity, credentialRequest, cancellationSignal,
            mainExecutor, credentialManagerCallback);
        var credentialResponse = await credentialManagerCallback.GetResultAsync().ConfigureAwait(false);
        return credentialResponse;
    }

    public async Task ClearCredentialStateAsync(Activity activity, CancellationToken cancellationToken)
    {
        var mainExecutor = activity.MainExecutor ?? throw new InvalidOperationException("Activity has no main executor.");
        using var request = new ClearCredentialStateRequest();
        using var credentialManagerCallback = new CredentialManagerCallback();
        var cancellationSignal = cancellationToken.ToCancellationSignal(); // do not dispose this
        credentialManager.ClearCredentialStateAsync(request, cancellationSignal,
            mainExecutor, credentialManagerCallback);
        await credentialManagerCallback.GetResultAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        credentialManager.Dispose();
    }
}
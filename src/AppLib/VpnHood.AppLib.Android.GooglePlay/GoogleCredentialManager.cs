using AndroidX.Credentials;

namespace VpnHood.AppLib.Droid.GooglePlay;

// The callback objects handed to Credential Manager are deliberately NOT disposed here. Android
// keeps calling them on the main executor for as long as it holds them, and disposing the managed
// peer first forces .NET to re-create it on the next call — which fails (no activation
// constructor) and crashes the process. The GC bridge releases them once the Java side lets go.
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
        var credentialManagerCallback = new CredentialManagerCallback();
        var cancellationSignal = cancellationToken.ToCancellationSignal(); // do not dispose this
        credentialManager.GetCredentialAsync(activity, credentialRequest, cancellationSignal,
            mainExecutor, credentialManagerCallback);
        var credentialResponse = await credentialManagerCallback.GetResultAsync().ConfigureAwait(false);
        return credentialResponse;
    }

    public async Task<CreateCredentialResponse> CreateCredentialAsync(Activity activity,
        CreateCredentialRequest credentialRequest, CancellationToken cancellationToken)
    {
        var mainExecutor = activity.MainExecutor ?? throw new InvalidOperationException("Activity has no main executor.");
        var createCredentialCallback = new CreateCredentialCallback();
        var cancellationSignal = cancellationToken.ToCancellationSignal(); // do not dispose this
        credentialManager.CreateCredentialAsync(activity, credentialRequest, cancellationSignal,
            mainExecutor, createCredentialCallback);
        var credentialResponse = await createCredentialCallback.GetResultAsync().ConfigureAwait(false);
        return credentialResponse;
    }

    public async Task ClearCredentialStateAsync(Activity activity, CancellationToken cancellationToken)
    {
        using var request = new ClearCredentialStateRequest();
        await ClearCredentialStateAsync(activity, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearCredentialStateAsync(Activity activity, ClearCredentialStateRequest request,
        CancellationToken cancellationToken)
    {
        var mainExecutor = activity.MainExecutor ?? throw new InvalidOperationException("Activity has no main executor.");
        var clearCredentialCallback = new ClearCredentialStateCallback();
        var cancellationSignal = cancellationToken.ToCancellationSignal(); // do not dispose this
        credentialManager.ClearCredentialStateAsync(request, cancellationSignal,
            mainExecutor, clearCredentialCallback);
        await clearCredentialCallback.GetResultAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        credentialManager.Dispose();
    }
}
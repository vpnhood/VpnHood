using AndroidX.Credentials;
using GetCredentialResponse = AndroidX.Credentials.GetCredentialResponse;
namespace VpnHood.Client.App.Droid.GooglePlay;

public class CredentialManagerCallback : Java.Lang.Object, ICredentialManagerCallback {
    private readonly TaskCompletionSource<GetCredentialResponse> _taskCompletionSource = new();
    public void OnError(Java.Lang.Object e)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        _taskCompletionSource.TrySetException((Exception)(object)e);
    }

    public void OnResult(Java.Lang.Object? result)
    {
        _taskCompletionSource.TrySetResult((GetCredentialResponse)result!);
    }

    public Task<GetCredentialResponse> GetResultAsync()
    {
        return _taskCompletionSource.Task;
    }
}
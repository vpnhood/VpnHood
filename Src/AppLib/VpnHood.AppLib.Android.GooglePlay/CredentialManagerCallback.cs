using AndroidX.Credentials;
using GetCredentialResponse = AndroidX.Credentials.GetCredentialResponse;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class CredentialManagerCallback : Java.Lang.Object, ICredentialManagerCallback
{
    private readonly TaskCompletionSource<GetCredentialResponse> _taskCompletionSource = new();

    public void OnError(Java.Lang.Object e)
    {
        if (e.Class.TypeName.Contains("CancellationException"))
            _taskCompletionSource.TrySetCanceled();
        else
            _taskCompletionSource.TrySetException(new Exception(e.ToString()));
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
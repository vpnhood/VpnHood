using AndroidX.Credentials;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.Droid.GooglePlay;

// Clear-credential completes with a Java Void result, so unlike
// CredentialManagerCallback any OnResult means success.
public class ClearCredentialStateCallback : Java.Lang.Object, ICredentialManagerCallback
{
    // completed from the Android main thread; the awaiting caller must not resume inside that callback frame
    private readonly TaskCompletionSource _taskCompletionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void OnError(Java.Lang.Object e)
    {
        _taskCompletionSource.TrySetException(new ApiException(
            new ApiError {
                TypeFullName = e.Class.TypeName,
                TypeName = e.Class.SimpleName,
                Message = e.ToString()
            }));
    }

    public void OnResult(Java.Lang.Object? result)
    {
        _taskCompletionSource.TrySetResult();
    }

    public Task GetResultAsync()
    {
        return _taskCompletionSource.Task;
    }
}

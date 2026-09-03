using AndroidX.Credentials;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.Droid.GooglePlay;

/// <summary>
/// The create-credential side of <see cref="CredentialManagerCallback" />: resolves to the
/// platform's CreateCredentialResponse. Used for the restore credential, whose creation is silent —
/// there is no sheet to dismiss, so unlike sign-in no error here means "the user changed their
/// mind"; every one is surfaced to the (best-effort) caller.
/// </summary>
public class CreateCredentialCallback : Java.Lang.Object, ICredentialManagerCallback
{
    // completed from the Android main thread; the awaiting caller must not resume inside that callback frame
    private readonly TaskCompletionSource<CreateCredentialResponse> _taskCompletionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void OnError(Java.Lang.Object e)
    {
        if (e.Class.TypeName.Contains("CancellationException")) {
            _taskCompletionSource.TrySetCanceled();
            return;
        }

        _taskCompletionSource.TrySetException(new ApiException(
            new ApiError {
                TypeFullName = e.Class.TypeName,
                TypeName = e.Class.SimpleName,
                Message = e.ToString()
            }));
    }

    public void OnResult(Java.Lang.Object? result)
    {
        if (result is CreateCredentialResponse credentialResponse)
            _taskCompletionSource.TrySetResult(credentialResponse);
        else
            _taskCompletionSource.TrySetException(
                new InvalidOperationException("Credential manager returned no create-credential response."));
    }

    public Task<CreateCredentialResponse> GetResultAsync()
    {
        return _taskCompletionSource.Task;
    }
}

using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Toolkit.ApiClients;
using GetCredentialResponse = AndroidX.Credentials.GetCredentialResponse;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class CredentialManagerCallback : Java.Lang.Object, ICredentialManagerCallback
{
    private readonly TaskCompletionSource<GetCredentialResponse> _taskCompletionSource = new();

    public void OnError(Java.Lang.Object e)
    {
        if (e.Class.SimpleName == "NoCredentialException")
            _taskCompletionSource.TrySetException(new NoCredentialException(e.ToString()));
        else if (e.Class.SimpleName == "GetCredentialCancellationException")
            _taskCompletionSource.TrySetException(new UserCanceledException(e.ToString()));
        else if (e.Class.TypeName.Contains("CancellationException"))
            _taskCompletionSource.TrySetCanceled();
        else
            _taskCompletionSource.TrySetException(new ApiException(
                new ApiError {
                    TypeFullName = e.Class.TypeName,
                    TypeName = e.Class.SimpleName,
                    Message = e.ToString()
                }));
    }

    public void OnResult(Java.Lang.Object? result)
    {
        if (result is GetCredentialResponse credentialResponse)
            _taskCompletionSource.TrySetResult(credentialResponse);
        else
            _taskCompletionSource.TrySetException(
                new InvalidOperationException("Credential manager returned no credential response."));
    }

    public Task<GetCredentialResponse> GetResultAsync()
    {
        return _taskCompletionSource.Task;
    }
}
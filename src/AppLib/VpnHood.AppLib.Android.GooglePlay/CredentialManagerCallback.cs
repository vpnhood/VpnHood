using System.Security.Authentication;
using System.Text.RegularExpressions;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;
using GetCredentialResponse = AndroidX.Credentials.GetCredentialResponse;

namespace VpnHood.AppLib.Droid.GooglePlay;

public partial class CredentialManagerCallback : Java.Lang.Object, ICredentialManagerCallback
{
    private readonly TaskCompletionSource<GetCredentialResponse> _taskCompletionSource = new();

    // Credential Manager reports real failures (stale account tokens, config problems)
    // as GetCredentialCancellationException — the same type as the user dismissing the
    // sheet — with the truth only in the message, e.g. "[16] Account reauth failed.".
    // A genuine dismissal carries no status code, so the code is what separates
    // "user changed their mind" (silent) from "something is broken" (must be shown).
    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex GmsStatusCodeRegex();

    public void OnError(Java.Lang.Object e)
    {
        VhLogger.Instance.LogWarning("Google credential manager error: {Type}: {Message}",
            e.Class.TypeName, e.ToString());

        if (e.Class.SimpleName == "NoCredentialException") {
            _taskCompletionSource.TrySetException(new NoCredentialException(e.ToString()));
        }
        else if (e.Class.SimpleName == "GetCredentialCancellationException") {
            var statusCode = GmsStatusCodeRegex().Match(e.ToString() ?? string.Empty);
            _taskCompletionSource.TrySetException(statusCode.Success
                ? new AuthenticationException(
                    $"Google sign-in failed (code {statusCode.Groups[1].Value}). " +
                    "Try re-adding your Google account in the device settings, then sign in again.")
                : new UserCanceledException(e.ToString()));
        }
        else if (e.Class.TypeName.Contains("CancellationException")) {
            _taskCompletionSource.TrySetCanceled();
        }
        else {
            _taskCompletionSource.TrySetException(new ApiException(
                new ApiError {
                    TypeFullName = e.Class.TypeName,
                    TypeName = e.Class.SimpleName,
                    Message = e.ToString()
                }));
        }
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
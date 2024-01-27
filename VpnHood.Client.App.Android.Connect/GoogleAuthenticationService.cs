using System.Net.Http.Headers;
using Android.Gms.Auth.Api.SignIn;
using VpnHood.Client.App.Droid.Connect.Properties;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid.Connect;

public class GoogleAuthenticationService : IAuthenticationService
{
    private const int SignInIntentId = 20200;
    private bool _disposed;
    private Activity? _activity;
    private readonly GoogleSignInClient _googleSignInClient;
    private TaskCompletionSource<GoogleSignInAccount>? _taskCompletionSource;

    public GoogleAuthenticationService(Activity activity)
    {
        _activity = activity;

        var googleSignInOptions = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestIdToken(AssemblyInfo.FirebaseClientId)
            .RequestEmail()
            .Build();

        _googleSignInClient = GoogleSignIn.GetClient(activity, googleSignInOptions);

        ((IActivityEvent)_activity).OnDestroyEvent += Activity_OnDestroy;
        ((IActivityEvent)_activity).OnActivityResultEvent += Activity_OnActivityResult;
    }

    public static GoogleAuthenticationService Create<T>(T activity) where T : Activity, IActivityEvent
    {
        return new GoogleAuthenticationService(activity);
    }


    public Task<AuthenticationHeaderValue?> TryGetAuthorization()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotImplementedException();
    }

    public async Task<AuthenticationHeaderValue> SignIn()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _taskCompletionSource = new TaskCompletionSource<GoogleSignInAccount>();
        _activity?.StartActivityForResult(_googleSignInClient.SignInIntent, SignInIntentId);
        var account = await _taskCompletionSource.Task;

        if (account.IdToken == null)
            throw new ArgumentNullException(account.IdToken);

        return new AuthenticationHeaderValue("Bearer", account.IdToken);
    }

    public Task SignOut()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _googleSignInClient.SignOutAsync();
    }

    private void Activity_OnActivityResult(object? sender, ActivityResultEventArgs e)
    {
        // If the request code is not related to the Google sign-in method
        if (e.RequestCode != SignInIntentId)
            return;

        GoogleSignIn.GetSignedInAccountFromIntentAsync(e.Data).ContinueWith((task) =>
        {
            if (task.IsCompletedSuccessfully) _taskCompletionSource?.TrySetResult(task.Result);
            else if (task.IsCanceled) _taskCompletionSource?.TrySetCanceled();
            else _taskCompletionSource?.TrySetException(task.Exception ?? new Exception("Could not signin with google."));
        });
    }

    private void Activity_OnDestroy(object? sender, EventArgs e)
    {
        _googleSignInClient.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _googleSignInClient.Dispose();
        _activity = null;
    }
}
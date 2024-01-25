using System.Net.Http.Headers;
using System.Text.Json;
using Android.Gms.Auth.Api.SignIn;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Droid.Connect.Properties;
using VpnHood.Client.Device.Droid.Utils;
using VpnHood.Common.Client;
using VpnHood.Store.Api;

namespace VpnHood.Client.App.Droid.Connect;

public class GoogleSignInService : IAppAccountService, IDisposable
{
    private const int SignInIntentId = 20200;
    private bool _disposed;
    private readonly HttpClient _storeHttpClient;
    private readonly GoogleSignInClient _googleSignInClient;
    private Activity? _activity;
    private TaskCompletionSource<GoogleSignInAccount>? _taskCompletionSource;
    private static string AccountFilePath => Path.Combine(VpnHoodApp.Instance.AppDataFolderPath, "account.json");

    public bool IsGoogleSignInSupported => true;

    private GoogleSignInService(Activity activity, HttpClient vpnHoodStoreStoreHttpClient)
    {
        _storeHttpClient = vpnHoodStoreStoreHttpClient;
        _activity = activity;

        var googleSignInOptions = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestIdToken(AssemblyInfo.FirebaseClientId)
            .RequestEmail()
            .Build();

        _googleSignInClient = GoogleSignIn.GetClient(activity, googleSignInOptions);

        ((IActivityEvent)_activity).OnDestroyEvent += Activity_OnDestroy;
        ((IActivityEvent)_activity).OnActivityResultEvent += Activity_OnActivityResult;
    }

    public static GoogleSignInService Create<T>(T activity, HttpClient httpClient) where T : Activity, IActivityEvent
    {
        return new GoogleSignInService(activity, httpClient);
    }

    public async Task SignInWithGoogle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _taskCompletionSource = new TaskCompletionSource<GoogleSignInAccount>();
        _activity?.StartActivityForResult(_googleSignInClient.SignInIntent, SignInIntentId);
        var account = await _taskCompletionSource.Task;

        if (account.IdToken == null)
            throw new ArgumentNullException(account.IdToken);

        await SignInToVpnHoodStore(account.IdToken);
    }

    public async Task<AppAccount> GetAccount()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var authenticationClient = new AuthenticationClient(_storeHttpClient);
        var currentUser = await authenticationClient.GetCurrentUserAsync();

        var currentVpnUserClient = new CurrentVpnUserClient(_storeHttpClient);
        var activeSubscription = await currentVpnUserClient.ListSubscriptionsAsync(AssemblyInfo.StoreAppId, false, false);
        var subscriptionPlanId = activeSubscription.SingleOrDefault()?.LastOrder;

        var appAccount = new AppAccount()
        {
            Email = currentUser.Email,
            Name = currentUser.Name,
            SubscriptionPlanId = subscriptionPlanId?.ProviderPlanId,
        };
        return appAccount;
    }

    private async Task SignInToVpnHoodStore(string idToken)
    {

        var authenticationClient = new AuthenticationClient(_storeHttpClient);
        try
        {
            var apiKey = await authenticationClient.SignInAsync(new SignInRequest
            {
                IdToken = idToken,
                RefreshTokenType = RefreshTokenType.None
            });
            await OnSignIn(apiKey);
        }
        catch (ApiException ex)
        {
            if (ex.ExceptionTypeName == "UnregisteredUserException")
                await SignUpToVpnHoodStore(idToken);
        }
    }

    private async Task SignUpToVpnHoodStore(string idToken)
    {
        var authenticationClient = new AuthenticationClient(_storeHttpClient);
        var apiKey = await authenticationClient.SignUpAsync(new SignUpRequest()
        {
            IdToken = idToken,
            RefreshTokenType = RefreshTokenType.None
        });
        await OnSignIn(apiKey);
    }

    private async Task OnSignIn(ApiKey apiKey)
    {
        // TODO Check expired api key
        _storeHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(apiKey.AccessToken.Scheme, apiKey.AccessToken.Value);
        var authenticationClient = new AuthenticationClient(_storeHttpClient);
        var currentUser = await authenticationClient.GetCurrentUserAsync();
        var localAccountFile = new LocalAccountFile()
        {
            ApiKey = apiKey,
            User = currentUser
        };
        await File.WriteAllTextAsync(AccountFilePath, JsonSerializer.Serialize(localAccountFile));
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

    private class LocalAccountFile
    {
        public required ApiKey ApiKey { get; set; }
        public required User User { get; set; }
    }
}
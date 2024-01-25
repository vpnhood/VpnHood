using Android.BillingClient.Api;
using System.Net;
using System.Text.Json;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Ads;
using Android.Gms.Auth.Api.SignIn;
using Android.Runtime;
using Android.Service.QuickSettings;
using Android.Views;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Droid.Common.Activities;
using VpnHood.Client.App.Droid.Connect.Properties;
using VpnHood.Client.App.Droid.GooglePlay;
using VpnHood.Common.Client;
using VpnHood.Store.Api;
using Java.Util;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace VpnHood.Client.App.Droid.Connect;

[Activity(Label = "@string/app_name",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    MainLauncher = true,
    Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize, // resize app when keyboard is shown
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.LayoutDirection |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.FontScale |
                           ConfigChanges.Locale | ConfigChanges.Navigation | ConfigChanges.UiMode)]

[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher])]
[IntentFilter([TileService.ActionQsTilePreferences])]

public class MainActivity : AndroidAppWebViewMainActivity, IAppAccountService
{
    private readonly HttpClient _storeHttpClient = App.StoreHttpClient;
    private GoogleSignInOptions _googleSignInOptions = default!;
    private GoogleSignInClient _googleSignInClient = default!;
    private static string AccountFilePath => Path.Combine(VpnHoodApp.Instance.AppDataFolderPath, "account.json");
    public bool IsGoogleSignInSupported => true;

    protected override IAppUpdaterService CreateAppUpdaterService()
    {
        return new GooglePlayAppUpdaterService(this);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        MobileAds.Initialize(this);
        VpnHoodApp.Instance.AccountService = this;
        VpnHoodApp.Instance.BillingService = GoogleBillingService.Create(this);

        // Signin with Google
        _googleSignInOptions = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestIdToken("216585339900-pc0j9nlkl15gqbtp95da1j6gvttm8aol.apps.googleusercontent.com")
            .RequestEmail()
            .Build();
        _googleSignInClient = GoogleSignIn.GetClient(this, _googleSignInOptions);
    }
    

    private TaskCompletionSource<GoogleSignInAccount>? _signInWithGoogleTaskCompletionSource;
    public async Task SignInWithGoogle()
    {
        var intent = _googleSignInClient.SignInIntent;
        _signInWithGoogleTaskCompletionSource = new TaskCompletionSource<GoogleSignInAccount>();
        StartActivityForResult(intent, 8585);
        var account = await _signInWithGoogleTaskCompletionSource.Task;

        if (account.IdToken == null)
            throw new ArgumentNullException(account.IdToken);

        await SignInToServer(account.IdToken);
    }

    private async Task SignInToServer(string idToken)
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
            {
                await SignUpToServer(idToken);
            }
        }
    }

    private async Task SignUpToServer(string idToken)
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

    public async Task<AppAccount> GetAccount()
    {
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

    // Google signin result
    protected override async void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        // If the request code is not related to the Google sign-in method
        if (requestCode != 8585)
            return;

        try
        {
            // Get account detail from Google.
            var account = await GoogleSignIn.GetSignedInAccountFromIntentAsync(data);

            // Return account detail.
            _signInWithGoogleTaskCompletionSource?.TrySetResult(account);
        }
        catch (Exception ex)
        {
            _signInWithGoogleTaskCompletionSource?.TrySetException(ex);
        }
    }

    protected override void OnDestroy()
    {
        VpnHoodApp.Instance.AccountService = null;
        VpnHoodApp.Instance.BillingService = null;
        base.OnDestroy();
    }

    private class LocalAccountFile
    {
        public required ApiKey ApiKey { get; set; }
        public required User User { get; set; }
    }
}
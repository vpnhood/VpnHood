using System.Net.Http.Headers;
using System.Text.Json;
using Android.Gms.Auth.Api.SignIn;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Droid.Connect.Properties;
using VpnHood.Client.Device.Droid.Utils;
using VpnHood.Common.Client;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Store.Api;

namespace VpnHood.Client.App.Droid.Connect;

public class AppAccountService : IAppAccountService, IDisposable
{
    private bool _disposed;
    private IAuthenticationService _authenticationService;
    private HttpClient _httpClient;
    private ApiKey? _apiKey;
    public static string AccountFilePath => Path.Combine(VpnHoodApp.Instance.AppDataFolderPath, "account.json");


    public bool IsGoogleSignInSupported => true;

    public AppAccountService(Uri storeBaseUrl, bool ignoreSslVerification, IAuthenticationService authenticationService)
    {
        var handler = new AuthenticatedClientHandler(authenticationService);

        if (ignoreSslVerification)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        _httpClient = new HttpClient(handler) { BaseAddress = storeBaseUrl };
        _apiKey = LoadApiKey(AccountFilePath);
    }


    public async Task SignInWithGoogle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var authenticationHeaderValue = await _authenticationService.SignIn();
        //await SignInToVpnHoodStore();
    }
    public Task SignOut()
    {
        throw new NotImplementedException();
    }

    public async Task<AppAccount> GetAccount()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var authenticationClient = new AuthenticationClient(_httpClient);
        var currentUser = await authenticationClient.GetCurrentUserAsync();

        var currentVpnUserClient = new CurrentVpnUserClient(_httpClient);
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

        var authenticationClient = new AuthenticationClient(_httpClient);
        try
        {
            var apiKey = await authenticationClient.SignInAsync(new SignInRequest
            {
                IdToken = idToken,
                RefreshTokenType = RefreshTokenType.None
            });
            await OnAfterSignIn(apiKey);
        }
        catch (ApiException ex)
        {
            if (ex.ExceptionTypeName == "UnregisteredUserException")
                await SignUpToVpnHoodStore(idToken);
        }
    }

    private async Task SignUpToVpnHoodStore(string idToken)
    {
        var authenticationClient = new AuthenticationClient(_httpClient);
        var apiKey = await authenticationClient.SignUpAsync(new SignUpRequest()
        {
            IdToken = idToken,
            RefreshTokenType = RefreshTokenType.None
        });
        await OnAfterSignIn(apiKey);
    }

    private async Task OnAfterSignIn(ApiKey apiKey)
    {
        // TODO Check expired api key
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(apiKey.AccessToken.Scheme, apiKey.AccessToken.Value);
        await File.WriteAllTextAsync(AccountFilePath, JsonSerializer.Serialize(apiKey));
    }

    private static ApiKey? LoadApiKey(string accountFilePath)
    {
        try
        {
            if (!File.Exists(accountFilePath))
                return null;

            var json =  File.ReadAllText(accountFilePath);
            var apiKey = VhUtil.JsonDeserialize<ApiKey>(json);
            return apiKey;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not read ApiKey file.");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _authenticationService.Dispose();
    }
}
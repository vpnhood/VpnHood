using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Abstractions;
using VpnHood.Common.Client;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Store.Api;

namespace VpnHood.Client.App.Store;

public class AppAuthenticationService : IAppAuthenticationService
{
    private bool _disposed;
    private readonly IAppAuthenticationExternalService? _externalAuthenticationService;
    private readonly HttpClient _httpClientWithoutAuth;
    private ApiKey? _apiKey;
    public static string AccountFilePath => Path.Combine(VpnHoodApp.Instance.AppDataFolderPath, "account.json");
    public bool IsSignInWithGoogleSupported => _externalAuthenticationService != null;
    public HttpClient HttpClient { get; }
    public Guid StoreAppId { get; }

    public AppAuthenticationService(
        Uri storeBaseUrl,
        Guid storeAppId,
        IAppAuthenticationExternalService? externalAuthenticationService,
        bool ignoreSslVerification = false)
    {
        _externalAuthenticationService = externalAuthenticationService;
        StoreAppId = storeAppId;
        var handlerWithAuth = new HttpClientHandlerAuth(this);
        if (ignoreSslVerification) handlerWithAuth.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        HttpClient = new HttpClient(handlerWithAuth) { BaseAddress = storeBaseUrl };

        var handlerWithoutAuth = new HttpClientHandler();
        if (ignoreSslVerification) handlerWithoutAuth.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _httpClientWithoutAuth = new HttpClient(handlerWithoutAuth) { BaseAddress = storeBaseUrl };

        _apiKey = LoadApiKey(AccountFilePath);
    }

    private async Task<ApiKey?> TryGetApiKey()
    {
        // null if it has not been signed in yet
        if (_apiKey == null)
            return null;

        // current key is valid
        if (_apiKey.AccessToken.ExpirationTime + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            return _apiKey;

        // refresh by refresh token
        if (_apiKey.RefreshToken != null && _apiKey.RefreshToken.ExpirationTime > DateTime.UtcNow)
        {
            var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
            _apiKey = await authenticationClient.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = _apiKey.RefreshToken.Value });
            await SaveApiKey(_apiKey);
            return _apiKey;
        }

        // refresh by id token
        var idToken = _externalAuthenticationService != null ? await _externalAuthenticationService.TrySilentSignIn() : null;
        if (idToken != null)
        {
            var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
            _apiKey = await authenticationClient.SignInAsync(new SignInRequest { IdToken = idToken });
            await SaveApiKey(_apiKey);
            return _apiKey;
        }

        await SignOut();
        throw new InvalidOperationException("Could not get refresh token.");// TODO check exception
    }

    public async Task SignInWithGoogle()
    {
        if (_externalAuthenticationService == null)
            throw new InvalidOperationException("Google sign in is not supported.");

        var idToken = await _externalAuthenticationService.SignIn();
        await SignInToVpnHoodStore(idToken, true);
    }

    public async Task SignOut()
    {
        if (File.Exists(AccountFilePath))
            File.Delete(AccountFilePath);

        if (_externalAuthenticationService == null)
            throw new InvalidOperationException("Could not sign out account. The authentication service is null.");

        await _externalAuthenticationService.SignOut();
    }

    private async Task SignInToVpnHoodStore(string idToken, bool autoSignUp)
    {
        var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
        try
        {
            var apiKey = await authenticationClient.SignInAsync(new SignInRequest
            {
                IdToken = idToken,
                RefreshTokenType = RefreshTokenType.None
            });
            await SaveApiKey(apiKey);
        }
        catch (ApiException ex)
        {
            if (ex.ExceptionTypeName == "UnregisteredUserException" && autoSignUp)
                await SignUpToVpnHoodStore(idToken);
        }
    }

    private async Task SignUpToVpnHoodStore(string idToken)
    {
        var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
        var apiKey = await authenticationClient.SignUpAsync(new SignUpRequest()
        {
            IdToken = idToken,
            RefreshTokenType = RefreshTokenType.None
        });
        await SaveApiKey(apiKey);
    }

    private static ApiKey? LoadApiKey(string accountFilePath)
    {
        try
        {
            if (!File.Exists(accountFilePath))
                return null;

            var json = File.ReadAllText(accountFilePath);
            var apiKey = VhUtil.JsonDeserialize<ApiKey>(json);
            return apiKey;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not read ApiKey file.");
            return null;
        }
    }

    private static Task SaveApiKey(ApiKey apiKey)
    {
        return File.WriteAllTextAsync(AccountFilePath, JsonSerializer.Serialize(apiKey));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _externalAuthenticationService?.Dispose();
        _httpClientWithoutAuth.Dispose();
        HttpClient.Dispose();
    }

    public class HttpClientHandlerAuth(AppAuthenticationService accountService) : HttpClientHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var apiKey = await accountService.TryGetApiKey();
            request.Headers.Authorization = apiKey != null ? new AuthenticationHeaderValue(apiKey.AccessToken.Scheme, apiKey.AccessToken.Value) : null;
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
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

    public string? UserId => ApiKey?.UserId;

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

    private ApiKey? ApiKey
    {
        get => _apiKey;
        set
        {
            _apiKey = value;
            if (value == null)
            {
                if (File.Exists(AccountFilePath))
                    File.Delete(AccountFilePath);
                return;
            }

            File.WriteAllText(AccountFilePath, JsonSerializer.Serialize(value));
        }
    }

    private async Task<ApiKey?> TryGetApiKey()
    {
        // null if it has not been signed in yet
        if (ApiKey == null)
            return null;

        // current key is valid
        if (ApiKey.AccessToken.ExpirationTime - TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            return ApiKey;

        try
        {
            // refresh by refresh token
            if (ApiKey.RefreshToken != null && ApiKey.RefreshToken.ExpirationTime < DateTime.UtcNow)
            {
                var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
                ApiKey = await authenticationClient.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = ApiKey.RefreshToken.Value });
                return ApiKey;
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not get refresh the access token.");
        }

        try
        {
            // refresh by id token
            var idToken = _externalAuthenticationService != null ? await _externalAuthenticationService.SilentSignIn() : null;
            if (!string.IsNullOrWhiteSpace(idToken))
            {
                var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
                ApiKey = await authenticationClient.SignInAsync(new SignInRequest { IdToken = idToken });
                return ApiKey;
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not refresh token by id token.");
        }

        return null;
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
        ApiKey = null;
        if (File.Exists(AccountFilePath))
            File.Delete(AccountFilePath);

        if (_externalAuthenticationService != null)
            await _externalAuthenticationService.SignOut();
    }

    private async Task SignInToVpnHoodStore(string idToken, bool autoSignUp)
    {
        var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
        try
        {
            ApiKey = await authenticationClient.SignInAsync(new SignInRequest
            {
                IdToken = idToken,
                RefreshTokenType = RefreshTokenType.None
            });
        }
        catch (ApiException ex)
        {
            if (ex.ExceptionTypeName == "UnregisteredUserException" && autoSignUp)
                await SignUpToVpnHoodStore(idToken);
            else
                throw;
        }
    }

    private async Task SignUpToVpnHoodStore(string idToken)
    {
        var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
        ApiKey = await authenticationClient.SignUpAsync(new SignUpRequest
        {
            IdToken = idToken,
            RefreshTokenType = RefreshTokenType.None
        });
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
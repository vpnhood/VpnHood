using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Store.Api;

namespace VpnHood.AppLib.Store;

public class StoreAuthenticationProvider : IAppAuthenticationProvider
{
    private bool _disposed;
    private readonly string _storageFolderPath;
    private readonly IAppAuthenticationExternalProvider? _authenticationExternalProvider;
    private readonly HttpClient _httpClientWithoutAuth;
    private ApiKey? _apiKey;
    private string ApiKeyFilePath => Path.Combine(_storageFolderPath, "account", "apiKey.json");
    public bool IsSignInWithGoogleSupported => _authenticationExternalProvider != null;

    public string? UserId => ApiKey?.UserId;

    public HttpClient HttpClient { get; }
    public Guid StoreAppId { get; }

    public StoreAuthenticationProvider(
        string storageFolderPath,
        Uri storeBaseUrl,
        Guid storeAppId,
        IAppAuthenticationExternalProvider? authenticationExternalProvider,
        bool ignoreSslVerification = false)
    {
        _storageFolderPath = storageFolderPath;
        _authenticationExternalProvider = authenticationExternalProvider;
        StoreAppId = storeAppId;
        var handlerWithAuth = new HttpClientHandlerAuth(this);
        if (ignoreSslVerification) handlerWithAuth.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        HttpClient = new HttpClient(handlerWithAuth) { BaseAddress = storeBaseUrl };

        var handlerWithoutAuth = new HttpClientHandler();
        if (ignoreSslVerification) handlerWithoutAuth.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _httpClientWithoutAuth = new HttpClient(handlerWithoutAuth) { BaseAddress = storeBaseUrl };

        _apiKey = JsonUtils.TryDeserializeFile<ApiKey>(ApiKeyFilePath, logger: VhLogger.Instance);
    }

    private ApiKey? ApiKey {
        get => _apiKey;
        set {
            _apiKey = value;
            if (value == null) {
                if (File.Exists(ApiKeyFilePath))
                    File.Delete(ApiKeyFilePath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ApiKeyFilePath)!);
            File.WriteAllText(ApiKeyFilePath, JsonSerializer.Serialize(value));
        }
    }

    private async Task<ApiKey?> TryGetApiKey(IUiContext? uiContext)
    {
        // null if it has not been signed in yet
        if (ApiKey == null)
            return null;

        // current key is valid
        if (ApiKey.AccessToken.ExpirationTime - TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            return ApiKey;

        try {
            // refresh by refresh token
            if (ApiKey.RefreshToken != null && ApiKey.RefreshToken.ExpirationTime < DateTime.UtcNow) {
                var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
                ApiKey = await authenticationClient
                    .RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = ApiKey.RefreshToken.Value })
                    .VhConfigureAwait();
                return ApiKey;
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not get refresh the access token.");
        }

        try {
            // refresh by id token
            if (uiContext == null)
                throw new Exception("UI context is not available.");

            var idToken = _authenticationExternalProvider != null
                ? await _authenticationExternalProvider.SignIn(uiContext, true).VhConfigureAwait()
                : null;

            if (!string.IsNullOrWhiteSpace(idToken)) {
                var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
                ApiKey = await authenticationClient.SignInAsync(new SignInRequest { IdToken = idToken })
                    .VhConfigureAwait();
                return ApiKey;
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not refresh token by id token.");
        }

        return null;
    }

    public async Task SignInWithGoogle(IUiContext uiContext)
    {
        if (_authenticationExternalProvider == null)
            throw new InvalidOperationException("Google sign in is not supported.");

        var idToken = await _authenticationExternalProvider.SignIn(uiContext, false).VhConfigureAwait();
        await SignInToVpnHoodStore(idToken, true).VhConfigureAwait();
    }

    public async Task SignOut(IUiContext uiContext)
    {
        ApiKey = null;
        if (File.Exists(ApiKeyFilePath))
            File.Delete(ApiKeyFilePath);


        if (_authenticationExternalProvider != null)
            await _authenticationExternalProvider.SignOut(uiContext).VhConfigureAwait();
    }

    private async Task SignInToVpnHoodStore(string idToken, bool autoSignUp)
    {
        var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
        try {
            ApiKey = await authenticationClient.SignInAsync(
                    new SignInRequest {
                        IdToken = idToken,
                        RefreshTokenType = RefreshTokenType.None
                    })
                .VhConfigureAwait();
        }
        // store must update its nuget package to support UnregisteredUserException
        catch (ApiException ex) {
            if (ex.ExceptionTypeName == "UnregisteredUserException" && autoSignUp)
                await SignUpToVpnHoodStore(idToken).VhConfigureAwait();
            else
                throw;
        }
    }

    private async Task SignUpToVpnHoodStore(string idToken)
    {
        var authenticationClient = new AuthenticationClient(_httpClientWithoutAuth);
        ApiKey = await authenticationClient.SignUpAsync(
                new SignUpRequest {
                    IdToken = idToken,
                    RefreshTokenType = RefreshTokenType.None
                })
            .VhConfigureAwait();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _authenticationExternalProvider?.Dispose();
        _httpClientWithoutAuth.Dispose();
        HttpClient.Dispose();
    }

    public class HttpClientHandlerAuth(StoreAuthenticationProvider accountProvider) : HttpClientHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var apiKey = await accountProvider.TryGetApiKey(AppUiContext.Context).VhConfigureAwait();
            request.Headers.Authorization = apiKey != null
                ? new AuthenticationHeaderValue(apiKey.AccessToken.Scheme, apiKey.AccessToken.Value)
                : null;
            return await base.SendAsync(request, cancellationToken).VhConfigureAwait();
        }
    }
}
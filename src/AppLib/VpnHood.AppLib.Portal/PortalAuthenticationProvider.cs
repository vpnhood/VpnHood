using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Portal.Dto;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// Signs in against the Portal API (POST /auth/sessions with a provider id
/// token) and holds the opaque session token: persisted like the Store package's
/// api key, injected into every request as a bearer plus X-Portal-Token (the
/// custom header survives proxies that strip Authorization).
/// </summary>
public class PortalAuthenticationProvider : IAppAuthenticationProvider
{
    private bool _disposed;
    private readonly IAppAuthenticationExternalProvider? _authenticationExternalProvider;
    private readonly HttpClient _httpClientWithoutAuth;
    private readonly string _packageName;
    private PortalSession? _session;
    private string SessionFilePath => Path.Combine(field, "account", "portalSession.json");

    public IReadOnlyList<AppSignInMethod> SignInMethods => _authenticationExternalProvider != null
        ? [AppSignInMethod.Google]
        : [];

    public string? UserId => Session?.UserId;
    public string? Email => Session?.Email;
    public HttpClient HttpClient { get; }

    public PortalAuthenticationProvider(
        string storageFolderPath,
        Uri portalBaseUrl,
        string packageName,
        IAppAuthenticationExternalProvider? authenticationExternalProvider,
        bool ignoreSslVerification = false)
    {
        SessionFilePath = storageFolderPath;
        _packageName = packageName;
        _authenticationExternalProvider = authenticationExternalProvider;

        var handlerWithAuth = new HttpClientHandlerAuth(this);
        if (ignoreSslVerification) handlerWithAuth.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        HttpClient = new HttpClient(handlerWithAuth) { BaseAddress = portalBaseUrl };

        var handlerWithoutAuth = new HttpClientHandler();
        if (ignoreSslVerification) handlerWithoutAuth.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _httpClientWithoutAuth = new HttpClient(handlerWithoutAuth) { BaseAddress = portalBaseUrl };

        _session = JsonUtils.TryDeserializeFile<PortalSession>(SessionFilePath, logger: VhLogger.Instance);
    }

    private PortalSession? Session {
        get => _session;
        set {
            _session = value;
            if (value == null) {
                if (File.Exists(SessionFilePath))
                    File.Delete(SessionFilePath);
                return;
            }

            var directoryPath = Path.GetDirectoryName(SessionFilePath)
                ?? throw new InvalidOperationException("Could not get the folder of the portal session file.");
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(SessionFilePath, JsonSerializer.Serialize(value));
        }
    }

    private async Task<PortalSession?> TryGetSession(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        // null if it has not been signed in yet
        if (Session == null)
            return null;

        // opaque tokens are long-lived; renew a day before expiry
        if (Session.ExpiresAt == null || Session.ExpiresAt - TimeSpan.FromDays(1) > DateTime.UtcNow)
            return Session;

        try {
            // silent re-sign-in with a fresh provider id token
            if (uiContext == null)
                throw new InvalidOperationException("UI context is not available.");
            if (_authenticationExternalProvider == null)
                return Session; // no way to renew; use it until the portal rejects it

            var idToken = await _authenticationExternalProvider.SignIn(uiContext, true, cancellationToken).Vhc();
            if (!string.IsNullOrWhiteSpace(idToken))
                return await SignInToPortal(idToken, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not renew the portal session silently.");
        }

        return Session;
    }

    public async Task SignIn(IUiContext uiContext, AppSignInOptions signInOptions, CancellationToken cancellationToken)
    {
        if (signInOptions.Method != AppSignInMethod.Google || _authenticationExternalProvider == null)
            throw new NotSupportedException($"Sign-in method is not supported. Method: {signInOptions.Method}");

        var idToken = await _authenticationExternalProvider.SignIn(uiContext, false, cancellationToken).Vhc();
        await SignInToPortal(idToken, cancellationToken).Vhc();
    }

    public async Task SignOut(IUiContext uiContext, CancellationToken cancellationToken)
    {
        // best-effort server-side revoke; local sign-out must succeed regardless
        var session = Session;
        if (session != null) {
            try {
                var apiClient = new PortalApiClient(HttpClient);
                await apiClient.DeleteCurrentSession(cancellationToken).Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Could not revoke the portal session.");
            }
        }
        Session = null; // the setter deletes the session file

        if (_authenticationExternalProvider != null)
            await _authenticationExternalProvider.SignOut(uiContext, cancellationToken).Vhc();
    }

    private async Task<PortalSession> SignInToPortal(string idToken, CancellationToken cancellationToken)
    {
        var apiClient = new PortalApiClient(_httpClientWithoutAuth);
        var response = await apiClient
            .CreateSession("google", idToken, _packageName, cancellationToken).Vhc();

        var session = new PortalSession {
            AccessToken = response.AccessToken,
            ExpiresAt = response.ExpiresAt,
            UserId = response.UserId,
            Email = response.Account.Email
        };
        Session = session;
        return session;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _authenticationExternalProvider?.Dispose();
        _httpClientWithoutAuth.Dispose();
        HttpClient.Dispose();
    }

    public class HttpClientHandlerAuth(PortalAuthenticationProvider authenticationProvider) : HttpClientHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var session = await authenticationProvider.TryGetSession(AppUiContext.Context, cancellationToken).Vhc();
            if (session != null) {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                request.Headers.Remove("X-Portal-Token");
                request.Headers.Add("X-Portal-Token", session.AccessToken);
            }
            else {
                request.Headers.Authorization = null;
            }
            return await base.SendAsync(request, cancellationToken).Vhc();
        }
    }
}

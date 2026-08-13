using System.Net;
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
    private readonly IReadOnlyList<IAppAuthenticationExternalProvider> _authenticationExternalProviders;
    private readonly HttpClient _httpClientWithoutAuth;
    private readonly string _packageName;
    private readonly Lock _sessionLock = new();
    private PortalSession? _session;
    private string SessionFilePath => Path.Combine(field, "account", "portalSession.json");

    // Pure pass-through of the external providers' self-declared method ids ("google" on Android,
    // "apple" on iOS, any id a third-party provider declares). This class has no identity-provider
    // knowledge of its own: AppSignInOptions.Method selects among these declarations, and the portal
    // wire discriminator is the selected declaration verbatim.
    public IReadOnlyList<string> SignInMethods { get; }

    public string? UserId => Session?.UserId;
    public string? Email => Session?.Email;
    public HttpClient HttpClient { get; }

    /// <summary>
    /// For the portal resources that take no session (sign-in, the plan catalog). Deliberately not
    /// <see cref="HttpClient" />: that one attaches the bearer and can stop to renew an expiring
    /// session — work an anonymous call should never wait on.
    /// </summary>
    public HttpClient HttpClientWithoutAuth => _httpClientWithoutAuth;

    public PortalAuthenticationProvider(
        string storageFolderPath,
        Uri portalBaseUrl,
        string packageName,
        IReadOnlyList<IAppAuthenticationExternalProvider> authenticationExternalProviders,
        bool ignoreSslVerification = false)
    {
        SessionFilePath = storageFolderPath;
        _packageName = packageName;
        _authenticationExternalProviders = authenticationExternalProviders;
        SignInMethods = authenticationExternalProviders.Select(x => x.SignInMethod).ToArray();
        if (SignInMethods.Distinct().Count() != SignInMethods.Count)
            throw new ArgumentException("Multiple external providers declare the same sign-in method id.",
                nameof(authenticationExternalProviders));

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
            // the file and the field must not disagree: a rejected session can now be dropped from an
            // in-flight request while the UI thread is signing in or out
            lock (_sessionLock) {
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
    }

    /// <summary>
    /// The portal answered 401: it does not know this session any more. There is no refresh token to
    /// fall back on, so the only honest local state is signed-out — the account was deleted, or its
    /// sessions revoked, somewhere else. Dropping the session here is what stops this device from
    /// showing a person the server has already forgotten.
    /// <para>
    /// The external identity provider is deliberately left signed in: revoking it needs a UI context
    /// no HTTP response handler has, and the next deliberate sign-in re-establishes everything anyway.
    /// </para>
    /// </summary>
    private void RejectSession(PortalSession rejectedSession)
    {
        lock (_sessionLock) {
            // a sign-in may have replaced the session while that request was in flight; only the
            // session the portal actually rejected is dropped
            if (_session == null || _session.AccessToken != rejectedSession.AccessToken)
                return;

            VhLogger.Instance.LogWarning(
                "The portal no longer accepts this session. Signing out on this device.");
            Session = null;
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

            // renew only via the provider that established the session; a different IdP could land in
            // a different account
            var externalProvider = FindExternalProvider(Session.SignInMethod);
            if (externalProvider == null)
                return Session; // no way to renew; use it until the portal rejects it

            var idToken = await externalProvider.SignIn(uiContext, true, cancellationToken).Vhc();
            if (!string.IsNullOrWhiteSpace(idToken))
                return await SignInToPortal(externalProvider, idToken, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not renew the portal session silently.");
        }

        return Session;
    }

    public async Task SignIn(IUiContext uiContext, AppSignInOptions signInOptions, CancellationToken cancellationToken)
    {
        // Method selects among the wired providers by their self-declared ids — never a hardcoded one.
        var externalProvider = FindExternalProvider(signInOptions.Method)
            ?? throw new NotSupportedException($"Sign-in method is not supported. Method: {signInOptions.Method}");

        var idToken = await externalProvider.SignIn(uiContext, false, cancellationToken).Vhc();
        await SignInToPortal(externalProvider, idToken, cancellationToken).Vhc();
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

        // sign out the provider that established the session; when that is unknown, all of them (best effort)
        var externalProviders = _authenticationExternalProviders
            .Where(x => session?.SignInMethod == null || x.SignInMethod == session.SignInMethod);
        foreach (var externalProvider in externalProviders)
            await externalProvider.SignOut(uiContext, cancellationToken).Vhc();
    }

    private async Task<PortalSession> SignInToPortal(IAppAuthenticationExternalProvider externalProvider,
        string idToken, CancellationToken cancellationToken)
    {
        // The wire discriminator tells the portal which IdP's keys verify this idToken. It is the
        // external provider's self-declared method id, sent VERBATIM — no mapping, no enumeration, so
        // a new provider (first- or third-party) needs no change here. Unknown ids are the portal's
        // to reject (fail-closed).
        var apiClient = new PortalApiClient(_httpClientWithoutAuth);
        var response = await apiClient
            .CreateSession(externalProvider.SignInMethod, idToken, _packageName, cancellationToken).Vhc();

        var session = new PortalSession {
            AccessToken = response.AccessToken,
            ExpiresAt = response.ExpiresAt,
            UserId = response.UserId,
            Email = response.Account.Email,
            SignInMethod = externalProvider.SignInMethod
        };
        Session = session;
        return session;
    }

    private IAppAuthenticationExternalProvider? FindExternalProvider(string? signInMethod)
    {
        return _authenticationExternalProviders.FirstOrDefault(x => x.SignInMethod == signInMethod);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var externalProvider in _authenticationExternalProviders)
            externalProvider.Dispose();
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

            var response = await base.SendAsync(request, cancellationToken).Vhc();

            // 401 is the portal saying "this token is not a session" — never a transport failure,
            // which throws instead of answering, and never a permission problem, which answers 403.
            // So this cannot sign anyone out over an outage.
            if (session != null && response.StatusCode == HttpStatusCode.Unauthorized)
                authenticationProvider.RejectSession(session);

            return response;
        }
    }
}

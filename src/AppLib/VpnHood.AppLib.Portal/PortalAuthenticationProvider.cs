using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Portal.Dto;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// Signs in against the Portal API (POST /auth/sessions with a provider id token) and holds the
/// opaque session token, persisted like the Store package's api key. It hands that token out on
/// request; attaching it to a call is the caller's business, so nothing here owns a client anyone
/// else uses.
/// </summary>
public class PortalAuthenticationProvider : IAuthenticationProvider
{
    private bool _disposed;
    private readonly IReadOnlyList<IAuthenticationExternalProvider> _authenticationExternalProviders;
    private readonly HttpClient _httpClient;
    private readonly string _packageName;
    private readonly Lock _sessionLock = new();
    private PortalSession? _session;
    private PortalSignInChallenge? _pendingChallenge;
    private string SessionFilePath => Path.Combine(field, "account", "portalSession.json");

    // The external providers' self-declared method ids ("google" on Android, "apple" on iOS, any id
    // a third-party provider declares), passed through verbatim — plus "password", which is the
    // portal's own credential form and needs no external provider on any platform. SignInOptions
    // .ProviderId selects among these, and the portal wire discriminator is the selection verbatim.
    public IReadOnlyList<string> ProviderIds { get; }

    public string? UserId => Session?.UserId;

    /// <summary>
    /// The account website is the portal's own host — the API lives under it (…/modules/addons/…),
    /// so the site root is where a person sets or recovers the client-area password.
    /// </summary>
    public Uri? AccountWebsiteUrl { get; }

    public PortalAuthenticationProvider(
        string storageFolderPath,
        Uri portalBaseUrl,
        string packageName,
        IReadOnlyList<IAuthenticationExternalProvider> authenticationExternalProviders,
        bool ignoreSslVerification = false)
    {
        SessionFilePath = storageFolderPath;
        _packageName = packageName;
        _authenticationExternalProviders = authenticationExternalProviders;
        ProviderIds = authenticationExternalProviders.Select(x => x.ProviderId)
            .Append(AuthProviders.Password)
            .ToArray();
        if (ProviderIds.Distinct().Count() != ProviderIds.Count)
            throw new ArgumentException("Multiple external providers declare the same sign-in method id.",
                nameof(authenticationExternalProviders));

        AccountWebsiteUrl = new Uri(portalBaseUrl.GetLeftPart(UriPartial.Authority));

        // Its own client, for its own two calls (sign in, revoke) — never handed out.
        var handler = new HttpClientHandler();
        if (ignoreSslVerification) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _httpClient = new HttpClient(handler) { BaseAddress = portalBaseUrl };

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
    public void InvalidateAccessToken(string accessToken)
    {
        lock (_sessionLock) {
            // a sign-in may have replaced the session while that request was in flight; only the
            // session the portal actually rejected is dropped
            if (_session == null || _session.AccessToken != accessToken)
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
            var externalProvider = FindExternalProvider(Session.ProviderId);
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

    /// <summary>
    /// The session token, silently renewed first when it is close to expiring. Null when nobody is
    /// signed in — an anonymous call asks for none, which is also why sign-in itself must not go
    /// through here: its 401 means "wrong credentials", not "dead session".
    /// </summary>
    public async Task<string?> GetAccessToken(CancellationToken cancellationToken)
    {
        var session = await TryGetSession(AppUiContext.Context, cancellationToken).Vhc();
        return session?.AccessToken;
    }

    public async Task<SignInResult> SignIn(IUiContext uiContext, SignInOptions signInOptions,
        CancellationToken cancellationToken)
    {
        if (signInOptions.ProviderId == AuthProviders.Password)
            return await SignInWithPassword(signInOptions, cancellationToken).Vhc();

        // Method selects among the wired providers by their self-declared ids — never a hardcoded one.
        var externalProvider = FindExternalProvider(signInOptions.ProviderId)
            ?? throw new NotSupportedException($"Sign-in provider is not supported. ProviderId: {signInOptions.ProviderId}");

        var idToken = await externalProvider.SignIn(uiContext, false, cancellationToken).Vhc();
        await SignInToPortal(externalProvider, idToken, cancellationToken).Vhc();
        return new SignInResult { State = SignInState.SignedIn };
    }

    /// <summary>
    /// The portal's own credential form (the account website's email + password) — two calls when
    /// the account uses a second factor: the first returns the challenge, the repeat carries only
    /// TwoFactorCode and completes the challenge held here. The portal never creates an account for
    /// this method, and its 401 is one answer for unknown email and wrong password alike.
    /// </summary>
    private async Task<SignInResult> SignInWithPassword(SignInOptions signInOptions,
        CancellationToken cancellationToken)
    {
        var apiClient = new PortalApiClient(_httpClient);
        PortalSignInResponse response;

        if (!string.IsNullOrEmpty(signInOptions.TwoFactorCode)) {
            var pendingChallenge = _pendingChallenge
                ?? throw new InvalidOperationException("There is no sign-in challenge to complete.");
            response = await apiClient.CompleteSessionChallenge(pendingChallenge.Token,
                signInOptions.TwoFactorCode, _packageName, cancellationToken).Vhc();
        }
        else {
            if (string.IsNullOrEmpty(signInOptions.UserName) || string.IsNullOrEmpty(signInOptions.Password))
                throw new ArgumentException("The password sign-in needs the email and the password.",
                    nameof(signInOptions));
            response = await apiClient.CreateSessionWithPassword(signInOptions.UserName,
                signInOptions.Password, _packageName, cancellationToken).Vhc();
        }

        // the password was right but a second factor is due: hold the challenge, sign nothing in.
        // A kind this build cannot prompt for is refused here — holding it would strand the person
        // on a dialog that can only ask the wrong question.
        if (response.Challenge != null) {
            var state = response.Challenge.Type switch {
                PortalSignInChallenge.TypeTotp => SignInState.TotpRequired,
                _ => throw UnsupportedTwoFactor(response.Challenge.Type)
            };
            _pendingChallenge = response.Challenge;
            return new SignInResult { State = state };
        }

        _pendingChallenge = null;
        Session = BuildSession(response, AuthProviders.Password);
        return new SignInResult {
            State = SignInState.SignedIn,
            NewBackupCode = response.NewBackupCode
        };
    }

    /// <summary>
    /// A second factor no dialog here can prompt for — an older build meeting a newer account. It
    /// travels with the same machine code the portal's own refusals use (Data["Code"], the UI's
    /// contract), because to the person it is the same kind of answer: this app cannot go on.
    /// </summary>
    private static NotSupportedException UnsupportedTwoFactor(string challengeType)
    {
        var exception = new NotSupportedException(
            $"This app cannot answer the account's second factor. Type: {challengeType}");
        exception.Data["Code"] = "unsupported_two_factor";
        return exception;
    }

    public async Task SignOut(IUiContext uiContext, CancellationToken cancellationToken)
    {
        // best-effort server-side revoke; local sign-out must succeed regardless
        var session = Session;
        if (session != null) {
            try {
                var apiClient = new PortalApiClient(_httpClient, this);
                await apiClient.DeleteCurrentSession(cancellationToken).Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Could not revoke the portal session.");
            }
        }
        Session = null; // the setter deletes the session file

        // Sign out the provider that established the session. With no session there is nothing to
        // target, so every provider is cleared instead — a local sign-out must leave no cached
        // credential behind, whatever state this device was in.
        var externalProviders = _authenticationExternalProviders
            .Where(x => session == null || x.ProviderId == session.ProviderId);
        foreach (var externalProvider in externalProviders)
            await externalProvider.SignOut(uiContext, cancellationToken).Vhc();
    }

    private async Task<PortalSession> SignInToPortal(IAuthenticationExternalProvider externalProvider,
        string idToken, CancellationToken cancellationToken)
    {
        // The wire discriminator tells the portal which IdP's keys verify this idToken. It is the
        // external provider's self-declared method id, sent VERBATIM — no mapping, no enumeration, so
        // a new provider (first- or third-party) needs no change here. Unknown ids are the portal's
        // to reject (fail-closed).
        var apiClient = new PortalApiClient(_httpClient);
        var response = await apiClient
            .CreateSession(externalProvider.ProviderId, idToken, _packageName, cancellationToken).Vhc();

        var session = BuildSession(response, externalProvider.ProviderId);
        Session = session;
        return session;
    }

    /// <summary>A sign-in answer that must BE a session (never a challenge) becomes the persisted one.</summary>
    private static PortalSession BuildSession(PortalSignInResponse response, string providerId)
    {
        return new PortalSession {
            AccessToken = response.AccessToken
                ?? throw new InvalidOperationException("The portal sign-in answer carries no session token."),
            ExpiresAt = response.ExpiresAt,
            UserId = response.UserId
                ?? throw new InvalidOperationException("The portal sign-in answer carries no user id."),
            ProviderId = providerId
        };
    }

    private IAuthenticationExternalProvider? FindExternalProvider(string providerId)
    {
        return _authenticationExternalProviders.FirstOrDefault(x => x.ProviderId == providerId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var externalProvider in _authenticationExternalProviders)
            externalProvider.Dispose();
        _httpClient.Dispose();
    }
}

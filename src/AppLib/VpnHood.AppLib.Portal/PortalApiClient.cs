using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.AppLib.Portal.Dto;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// The typed stub of the Portal REST API: one method per operation the app calls, named after
/// the operationIds in the portal's openapi.json, so callers never see a path,
/// a verb or a wire shape. Operations meant for an operator rather than an app — /system/status
/// is the only one — have no method here; the app never probes what it is already talking to.
/// All transport comes from the shared ApiClientBase —
/// including failures: the portal's problem+json is recognized there and arrives
/// as the standard ApiException, machine code in Data["Code"]. What the Portal
/// does differently fits in the constructor and one override: resources append
/// to an endpoint script (PATH_INFO), and the wire is camelCase.
/// </summary>
public class PortalApiClient : ApiClientBase
{
    /// <summary>
    /// The major version every path below hangs off, added to the base address once so the paths
    /// stay bare. A published app can never be force-updated, so when this API has to change
    /// incompatibly the portal serves /v2 beside /v1 and installed apps keep working untouched —
    /// which is why the segment lives in code, not in configuration: which contract a build speaks
    /// is a fact about the build. It tracks the major of the portal's own contract version.
    /// </summary>
    private const string ApiVersion = "v1";

    private readonly IAuthenticationProvider? _authenticationProvider;

    /// <param name="httpClient">The HTTP client used to make requests to the portal API.</param>
    /// <param name="authenticationProvider">
    /// Attaches the session credential to every call made through this instance, and invalidates it
    /// when the portal refuses it. Omit it for the resources that take no session — sign-in and the
    /// product catalog — so a 401 from wrong credentials can never be mistaken for a dead session.
    /// </param>
    public PortalApiClient(HttpClient httpClient, IAuthenticationProvider? authenticationProvider = null)
        : base(httpClient)
    {
        _authenticationProvider = authenticationProvider;
        // api.php is an endpoint script that resources append to (PATH_INFO); Uri
        // combining only appends when the base ends with a slash, so normalize it
        // here once and keep every path below bare-relative.
        var baseAddress = httpClient.BaseAddress
            ?? throw new InvalidOperationException("The portal base address has not been set.");
        DefaultBaseAddress = new Uri($"{baseAddress.AbsoluteUri.TrimEnd('/')}/{ApiVersion}/");
        Logger = VhLogger.Instance;
    }

    /// <summary>POST /auth/sessions — sign in with a provider id token; returns the opaque session.</summary>
    public Task<PortalSignInResponse> CreateSession(string provider, string idToken, string packageName,
        CancellationToken cancellationToken)
    {
        return HttpPostAsync<PortalSignInResponse>("auth/sessions", null,
            new { provider, idToken, packageName }, cancellationToken);
    }

    /// <summary>
    /// POST /auth/sessions, the password form — the account website's own email + password. Answers
    /// the session, or only a Challenge when a second factor is due. Sign-in only: the portal never
    /// creates an account for this form, and an unknown email is indistinguishable from a wrong
    /// password (`invalid_credentials`) by design.
    /// </summary>
    public Task<PortalSignInResponse> CreateSessionWithPassword(string email, string password, string packageName,
        CancellationToken cancellationToken)
    {
        return HttpPostAsync<PortalSignInResponse>("auth/sessions", null,
            new { email, password, packageName }, cancellationToken);
    }

    /// <summary>
    /// POST /auth/sessions, the challenge form — completes the password form's second factor with
    /// the authenticator code or the backup code. A spent backup code comes back rotated
    /// (NewBackupCode, shown once). 401 `invalid_code` while attempts remain; `invalid_challenge`
    /// when the token is expired or spent (start over from the password).
    /// </summary>
    public Task<PortalSignInResponse> CompleteSessionChallenge(string challengeToken, string code, string packageName,
        CancellationToken cancellationToken)
    {
        return HttpPostAsync<PortalSignInResponse>("auth/sessions", null,
            new { challengeToken, code, packageName }, cancellationToken);
    }

    /// <summary>DELETE /auth/sessions/current — sign out. Idempotent on the portal side.</summary>
    public Task DeleteCurrentSession(CancellationToken cancellationToken)
    {
        return HttpDeleteAsync("auth/sessions/current", null, cancellationToken);
    }

    /// <summary>
    /// POST /auth/sessions, the restore-credential form — a WebAuthn assertion from the restore key
    /// this device carried over from its predecessor (zero-tap sign-in restoration). Sign-in only;
    /// every failure is one neutral 401 `invalid_restore_credential`.
    /// </summary>
    public Task<PortalSignInResponse> CreateSessionWithRestoreCredential(string assertionResponseJson,
        string packageName, CancellationToken cancellationToken)
    {
        return HttpPostAsync<PortalSignInResponse>("auth/sessions", null,
            new { assertionResponseJson, packageName }, cancellationToken);
    }

    /// <summary>
    /// POST /auth/restore-credentials/registration-options — WebAuthn creation options for this
    /// device's restore key. Session-authenticated: the session is the trust root of the
    /// registration. The RequestJson goes to the platform credential API verbatim.
    /// </summary>
    public Task<PortalRestoreCredentialOptions> CreateRestoreCredentialRegistrationOptions(
        CancellationToken cancellationToken)
    {
        // data must be an empty object, not null: null serializes to the JSON literal `null`,
        // which the portal rightly refuses as a body
        return HttpPostAsync<PortalRestoreCredentialOptions>("auth/restore-credentials/registration-options",
            null, new { }, cancellationToken);
    }

    /// <summary>
    /// POST /auth/restore-credentials — store the key the platform just registered. Re-registering
    /// an existing credential replaces it in place, so calling this on every sign-in is safe.
    /// </summary>
    public Task<PortalRestoreCredentialRegistered> CreateRestoreCredential(string responseJson,
        CancellationToken cancellationToken)
    {
        return HttpPostAsync<PortalRestoreCredentialRegistered>("auth/restore-credentials", null,
            new { responseJson }, cancellationToken);
    }

    /// <summary>
    /// POST /auth/restore-credentials/assertion-options — WebAuthn request options for the zero-tap
    /// sign-in. Anonymous by nature (nobody is signed in yet), app-gated like sign-in itself.
    /// </summary>
    public Task<PortalRestoreCredentialOptions> CreateRestoreCredentialAssertionOptions(string packageName,
        CancellationToken cancellationToken)
    {
        return HttpPostAsync<PortalRestoreCredentialOptions>("auth/restore-credentials/assertion-options",
            null, new { packageName }, cancellationToken);
    }

    /// <summary>
    /// DELETE /auth/restore-credentials?credentialId=… — retire this device's restore key on
    /// sign-out, alongside clearing it locally. Idempotent on the portal side.
    /// </summary>
    public Task DeleteRestoreCredential(string credentialId, CancellationToken cancellationToken)
    {
        return HttpDeleteAsync("auth/restore-credentials",
            new Dictionary<string, object?> { ["credentialId"] = credentialId }, cancellationToken);
    }

    /// <summary>
    /// GET /account — the complete snapshot, and the wire maps <see cref="Account" /> 1:1, so it
    /// deserializes straight into the app model: identity, THE one access code serving the account
    /// (server-ranked — an active subscription's code outranks the website choice; the app never
    /// picks), and the subscription behind it. Only <see cref="Subscription.Management" /> is absent
    /// on the wire — it is composed by the caller from its own billing provider.
    /// </summary>
    public Task<Account> GetAccount(CancellationToken cancellationToken)
    {
        return HttpGetAsync<Account>("account", null, cancellationToken);
    }

    /// <summary>
    /// DELETE /account: the portal erases the person everywhere (all sessions, all
    /// identities, the account itself). Nothing blocks it — website billing is cancelled at the end
    /// of its paid period portal-side, and a store subscription is deliberately left untouched:
    /// signing in again brings it back by itself.
    /// </summary>
    public Task DeleteAccount(CancellationToken cancellationToken)
    {
        return HttpDeleteAsync("account", null, cancellationToken);
    }

    /// <summary>
    /// PUT /account/access-code — upload a code into the account's ONE slot, or empty it with null.
    /// The portal takes the code on trust and never looks it up to approve it, so there is no
    /// <c>code_not_found</c> to catch here any more and the only failure is a transport one. The
    /// answer carries nothing: what the account serves afterwards is read from GET /account, and it
    /// need not be the code just uploaded.
    /// </summary>
    public Task SetAccessCode(string? accessCode, CancellationToken cancellationToken)
    {
        return HttpPutAsync("account/access-code", null, new { accessCode }, cancellationToken);
    }

    /// <summary>
    /// POST /account/access-code/rejected — tell the portal the access server refused the code it
    /// is serving. The code rides in the BODY and never in the path: a URL is logged, cached and
    /// proxied in places a bearer credential must not appear. Answers 204 whether or not the report
    /// still applies, so there is nothing here to inspect.
    /// </summary>
    public Task ReportAccessCodeRejected(string accessCode, CancellationToken cancellationToken)
    {
        return HttpPostAsync("account/access-code/rejected", null,
            new { accessCode }, cancellationToken);
    }

    /// <summary>
    /// GET /billing/products — the distinct store product ids this app may sell in that store. The
    /// app asks its own store to price them, and the store itself enumerates the base plans within
    /// a product, so a plan never appears here in its own right.
    /// </summary>
    public Task<IReadOnlyList<string>> ListProducts(string storeId, string packageName,
        CancellationToken cancellationToken)
    {
        // "store" in the query, "storeId" in a body: a query is a filter over a closed vocabulary,
        // beside packageName, while a JSON field names a value on a modelled object.
        return HttpGetAsync<IReadOnlyList<string>>("billing/products",
            new Dictionary<string, object?> { ["store"] = storeId, ["packageName"] = packageName },
            cancellationToken);
    }

    /// <summary>
    /// GET /billing/plans — the priced plans a WEB-distributed app offers, each with a ready-made
    /// checkout URL. The portal refuses store-distributed apps (their store prices their plans),
    /// and prices everything in one currency that each purchase URL pins — called with the session
    /// attached when one exists, so a signed-in account is priced in its own locked currency.
    /// </summary>
    public Task<IReadOnlyList<PortalPlan>> ListPlans(string storeId, string packageName,
        CancellationToken cancellationToken)
    {
        return HttpGetAsync<IReadOnlyList<PortalPlan>>("billing/plans",
            new Dictionary<string, object?> { ["store"] = storeId, ["packageName"] = packageName },
            cancellationToken);
    }

    /// <summary>
    /// POST /billing/purchases — redeem a store purchase. The answer is the state alone: once
    /// provisioned, the caller refreshes GET /account, which is where the delivered code and the
    /// subscription live. Each store proves a purchase its own way — Play hands out a purchase
    /// token, StoreKit 2 a signed transaction — and that wire knowledge lives here so callers pass
    /// the raw purchase data and nothing else. The proof is only a pointer: the portal re-fetches
    /// the purchase from the store.
    /// </summary>
    public Task<PortalPurchaseState> CreatePurchase(string storeId, string packageName, string purchaseProof,
        CancellationToken cancellationToken)
    {
        object proof = storeId == StoreIds.AppStore
            ? new { jws = purchaseProof }
            : new { purchaseToken = purchaseProof };

        return HttpPostAsync<PortalPurchaseState>("billing/purchases", null,
            new { storeId, packageName, proof }, cancellationToken);
    }

    /// <summary>
    /// The portal's wire is camelCase; the toolkit default is PascalCase and
    /// case-sensitive, which would bind nothing at all.
    /// </summary>
    protected override JsonSerializerOptions CreateSerializerSettings()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <summary>
    /// The session credential travels twice on purpose: as the standard bearer, and as X-Portal-Token
    /// for the proxies that strip Authorization. A 401 is the portal saying "this token is not a
    /// session" — never a transport failure, which throws instead of answering, and never a
    /// permission problem, which answers 403 — so reporting it here cannot sign anyone out over an
    /// outage.
    /// </summary>
    protected override async Task<HttpResponseMessage> HttpClientSendAsync(HttpClient client,
        HttpRequestMessage request, HttpCompletionOption responseHeadersRead,
        CancellationToken cancellationToken)
    {
        var authenticationProvider = _authenticationProvider;
        if (authenticationProvider == null)
            return await base.HttpClientSendAsync(client, request, responseHeadersRead, cancellationToken).Vhc();

        var accessToken = await authenticationProvider.GetAccessToken(cancellationToken).Vhc();
        if (accessToken != null) {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Remove("X-Portal-Token");
            request.Headers.Add("X-Portal-Token", accessToken);
        }

        var response = await base.HttpClientSendAsync(client, request, responseHeadersRead, cancellationToken).Vhc();
        if (accessToken != null && response.StatusCode == HttpStatusCode.Unauthorized)
            authenticationProvider.InvalidateAccessToken(accessToken);

        return response;
    }
}

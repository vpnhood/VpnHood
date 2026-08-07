using System.Text.Json;
using VpnHood.AppLib.Portal.Dto;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// The typed stub of the Portal REST API: one method per operation, named after
/// the operationIds in the portal's openapi.json, so callers never see a path,
/// a verb or a wire shape. All transport comes from the shared ApiClientBase —
/// including failures: the portal's problem+json is recognized there and arrives
/// as the standard ApiException, machine code in Data["Code"]. What the Portal
/// does differently fits in the constructor and one override: resources append
/// to an endpoint script (PATH_INFO), and the wire is camelCase.
/// </summary>
public class PortalApiClient : ApiClientBase
{
    public PortalApiClient(HttpClient httpClient) : base(httpClient)
    {
        // api.php is an endpoint script that resources append to (PATH_INFO); Uri
        // combining only appends when the base ends with a slash, so normalize it
        // here once and keep every path below bare-relative.
        var baseAddress = httpClient.BaseAddress
            ?? throw new InvalidOperationException("The portal base address has not been set.");
        DefaultBaseAddress = new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + "/");
        Logger = VhLogger.Instance;
    }

    /// <summary>GET /system/status — liveness. A problem 404 means the portal is not activated on that install.</summary>
    public Task<PortalStatus> GetStatus(CancellationToken cancellationToken)
    {
        return HttpGetAsync<PortalStatus>("system/status", null, cancellationToken);
    }

    /// <summary>POST /auth/sessions — sign in with a provider id token; returns the opaque session.</summary>
    public Task<PortalSignInResponse> CreateSession(string provider, string idToken, string packageName,
        CancellationToken cancellationToken)
    {
        return HttpPostAsync<PortalSignInResponse>("auth/sessions", null,
            new { provider, idToken, packageName }, cancellationToken);
    }

    /// <summary>DELETE /auth/sessions/current — sign out. Idempotent on the portal side.</summary>
    public Task DeleteCurrentSession(CancellationToken cancellationToken)
    {
        return HttpDeleteAsync("auth/sessions/current", null, cancellationToken);
    }

    /// <summary>GET /account — the signed-in account.</summary>
    public Task<PortalAccountInfo> GetAccount(CancellationToken cancellationToken)
    {
        return HttpGetAsync<PortalAccountInfo>("account", null, cancellationToken);
    }

    /// <summary>GET /account/entitlements — what the signed-in account currently holds.</summary>
    public Task<PortalEntitlementList> ListEntitlements(CancellationToken cancellationToken)
    {
        return HttpGetAsync<PortalEntitlementList>("account/entitlements", null, cancellationToken);
    }

    /// <summary>GET /billing/plans — the plans this app may sell in that store.</summary>
    public Task<PortalPlanList> ListPlans(string store, string packageName, CancellationToken cancellationToken)
    {
        return HttpGetAsync<PortalPlanList>("billing/plans",
            new Dictionary<string, object?> { ["store"] = store, ["packageName"] = packageName }, cancellationToken);
    }

    /// <summary>
    /// POST /billing/purchases — redeem a store purchase into an entitlement, access
    /// code included. Each store proves a purchase its own way — Play hands out a
    /// purchase token, StoreKit 2 a signed transaction — and that wire knowledge
    /// lives here so callers pass the raw purchase data and nothing else. The proof
    /// is only a pointer: the portal re-fetches the purchase from the store.
    /// </summary>
    public Task<PortalEntitlement> CreatePurchase(string store, string packageName, string purchaseData,
        CancellationToken cancellationToken)
    {
        object proof = store == PortalStoreIds.AppStore
            ? new { jws = purchaseData }
            : new { purchaseToken = purchaseData };

        return HttpPostAsync<PortalEntitlement>("billing/purchases", null,
            new { store, packageName, proof }, cancellationToken);
    }

    /// <summary>
    /// The portal's wire is camelCase; the toolkit default is PascalCase and
    /// case-sensitive, which would bind nothing at all.
    /// </summary>
    protected override JsonSerializerOptions CreateSerializerSettings()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }
}

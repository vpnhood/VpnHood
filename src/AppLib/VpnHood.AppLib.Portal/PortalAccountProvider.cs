using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Portal.Dto;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// Account facade over the Portal API. Entitlements carry their access code
/// directly (GET /account/entitlements) — no token-list walking; the portal never
/// exposes backend ids on the wire.
/// </summary>
/// <param name="fallbackProductIds">
/// The build's own store product ids, used only while the portal cannot answer — see
/// <see cref="GetProductIds" />. The portal is the catalog; this is the offline/first-run
/// stand-in so an unreachable backend cannot empty the plans page.
/// </param>
public class PortalAccountProvider(
    PortalAuthenticationProvider authenticationProvider,
    IAppBillingProvider? billingProvider,
    string storeId,
    string packageName,
    IReadOnlyList<string> fallbackProductIds)
    : IAppAccountProvider, IDisposable
{
    /// <summary>The SubscriptionId the app model uses when a portal entitlement is active.</summary>
    public const string PortalSubscriptionId = "portal";

    public IAppAuthenticationProvider AuthenticationProvider { get; } = authenticationProvider;

    public AppBilling? Billing { get; } = billingProvider != null
        ? new AppBilling {
            Provider = billingProvider,
            OrderProcessor = new PortalOrderProcessor(authenticationProvider, storeId, packageName)
        }
        : null;

    /// <summary>
    /// The sellable products according to the portal (GET /billing/plans), which is where the
    /// mapping from a store product to a plan already lives: a product the portal does not map
    /// cannot be redeemed, so asking it — rather than trusting the build's own list — is what keeps
    /// a purchase from landing on a plan the backend has never heard of. Read anonymously: the
    /// resource takes no session, and a plans page renders before anyone signs in.
    /// <para>
    /// The fallback ids stand in whenever the portal cannot answer (offline, first run, an older
    /// module): a backend outage must not empty the plans page. An answered-but-empty catalog is
    /// honoured as given — the portal saying "nothing is sellable here" is an answer, and falling
    /// back there would offer products no payment could be redeemed against.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken)
    {
        try {
            var apiClient = new PortalApiClient(authenticationProvider.HttpClientWithoutAuth);
            var plans = await apiClient.ListPlans(storeId, packageName, cancellationToken).Vhc();

            // a store product may carry several plans (Play base plans); the store is queried per product
            var productIds = plans.Items.Select(x => x.StoreProductId).Distinct().ToArray();
            if (productIds.Length == 0)
                VhLogger.Instance.LogWarning(
                    "The portal maps no sellable product for this app. Store: {Store}, PackageName: {PackageName}",
                    storeId, packageName);

            return productIds;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "Could not read the product catalog from the portal; falling back to the embedded ids. " +
                "Store: {Store}, PackageName: {PackageName}", storeId, packageName);
            return fallbackProductIds;
        }
    }

    public async Task<AppAccount?> GetAccount(CancellationToken cancellationToken)
    {
        if (AuthenticationProvider.UserId == null)
            return null;

        var apiClient = new PortalApiClient(AuthenticationProvider.HttpClient);
        var me = await apiClient.GetAccount(cancellationToken).Vhc();
        var entitlement = await TryGetEntitlement(apiClient, cancellationToken).Vhc();

        return new AppAccount {
            UserId = me.UserId,
            Email = me.Account.Email,
            SubscriptionId = entitlement != null ? PortalSubscriptionId : null,
            ProviderPlanId = entitlement?.PlanId,
            ExpirationTime = entitlement?.ExpiresAt,
            // this provider has no account-level record to expose, so the account IS the
            // subscription here: CreatedTime is when the subscription started, and the
            // price is the store's own charge for the current period
            CreatedTime = entitlement?.PurchasedAt,
            IsAutoRenew = entitlement?.AutoRenewing,
            PriceAmount = entitlement?.PriceAmount,
            PriceCurrency = entitlement?.PriceCurrency,
            PriceBillingPeriod = entitlement?.BillingPeriod,
            // the build's store page is offered only when that same store billed the entitlement;
            // a cross-store subscription gets no link and the UI falls back to a neutral sentence
            SubscriptionManagementUrl = entitlement?.Store == storeId
                ? billingProvider?.SubscriptionManagementUrl
                : null
        };
    }

    public Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken)
    {
        // the portal delivers access codes, never raw access keys
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public async Task DeleteAccount(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var apiClient = new PortalApiClient(AuthenticationProvider.HttpClient);
        await apiClient.DeleteAccount(cancellationToken).Vhc();

        // The account is gone server-side; make this device forget it too. SignOut deletes the
        // session file and drops the external IdP's cached credential, so the next sign-in is a
        // deliberate act that knowingly creates a brand-new account. Its server-side revoke is a
        // harmless 204 — the portal already deleted every session.
        await authenticationProvider.SignOut(uiContext, cancellationToken).Vhc();
    }

    public async Task<string> GetAccessCode(string subscriptionId, CancellationToken cancellationToken)
    {
        var apiClient = new PortalApiClient(AuthenticationProvider.HttpClient);
        var entitlement = await TryGetEntitlement(apiClient, cancellationToken).Vhc();
        return entitlement?.AccessCode
            ?? throw new InvalidOperationException("There is no delivered entitlement for this account.");
    }

    private static async Task<PortalEntitlement?> TryGetEntitlement(PortalApiClient apiClient,
        CancellationToken cancellationToken)
    {
        var entitlements = await apiClient.ListEntitlements(cancellationToken).Vhc();
        return entitlements.Items.FirstOrDefault(x => x.AccessCode != null);
    }

    public void Dispose()
    {
        Billing?.Provider.Dispose();
        AuthenticationProvider.Dispose();
    }
}

using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Portal.Dto;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// Account facade over the Portal API. Entitlements carry their access code
/// directly (GET /account/entitlements) — no token-list walking; the portal never
/// exposes backend ids on the wire.
/// </summary>
public class PortalAccountProvider(
    PortalAuthenticationProvider authenticationProvider,
    IAppBillingProvider? billingProvider,
    string storeId,
    string packageName)
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

    public async Task<AppAccount?> GetAccount(CancellationToken cancellationToken)
    {
        if (AuthenticationProvider.UserId == null)
            return null;

        var apiClient = new PortalApiClient(AuthenticationProvider.HttpClient);
        var me = await apiClient.Get<PortalAccountInfo>("/account", cancellationToken).Vhc();
        var entitlement = await TryGetEntitlement(apiClient, cancellationToken).Vhc();

        return new AppAccount {
            UserId = me.UserId,
            Email = me.Account.Email,
            SubscriptionId = entitlement != null ? PortalSubscriptionId : null,
            ProviderPlanId = entitlement?.PlanId,
            ExpirationTime = entitlement?.ExpiresAt
        };
    }

    public Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken)
    {
        // the portal delivers access codes, never raw access keys
        return Task.FromResult<IReadOnlyList<string>>([]);
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
        var entitlements = await apiClient
            .Get<PortalEntitlementList>("/account/entitlements", cancellationToken).Vhc();
        return entitlements.Items.FirstOrDefault(x => x.AccessCode != null);
    }

    public void Dispose()
    {
        Billing?.Provider.Dispose();
        AuthenticationProvider.Dispose();
    }
}

using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.Billing;

namespace VpnHood.App.StoreScreenshots;

// Nothing is for sale in a screenshot.
internal sealed class FakeBillingApi : IBillingApi
{
    public Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Billing.GetSubscriptionPlans");
    public Task Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken) => throw UnmockedCalls.Record("Billing.Purchase");
    public Task<bool> RestorePurchase(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Billing.RestorePurchase");
    public Task OpenSubscriptionManagement(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Billing.OpenSubscriptionManagement");
}

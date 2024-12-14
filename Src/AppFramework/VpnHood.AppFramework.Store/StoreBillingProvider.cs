using VpnHood.AppFramework.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.Utils;

namespace VpnHood.AppFramework.Store;

internal class StoreBillingProvider(StoreAccountProvider storeAccountProvider, IAppBillingProvider billingProvider)
    : IAppBillingProvider
{
    public void Dispose()
    {
        billingProvider.Dispose();
    }

    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return billingProvider.GetSubscriptionPlans();
    }

    public async Task<string> Purchase(IUiContext uiContext, string planId)
    {
        try {
            PurchaseState = BillingPurchaseState.Started;
            var providerOrderId = await billingProvider.Purchase(uiContext, planId).VhConfigureAwait();
            PurchaseState = BillingPurchaseState.Processing;
            await storeAccountProvider.WaitForProcessProviderOrder(providerOrderId).VhConfigureAwait();
            return providerOrderId;
        }
        finally {
            PurchaseState = BillingPurchaseState.None;
        }
    }

    public BillingPurchaseState PurchaseState { get; private set; }
}
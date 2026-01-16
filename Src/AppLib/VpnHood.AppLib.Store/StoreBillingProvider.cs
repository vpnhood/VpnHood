using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Store.Api;

namespace VpnHood.AppLib.Store;

internal class StoreBillingProvider(
    Guid storeAppId,
    IAppAuthenticationProvider appAuthenticationProvider,
    IAppBillingProvider billingProvider)
    : IAppBillingProvider
{
    public BillingPurchaseState PurchaseState { get; private set; }

    public string ProviderName => billingProvider.ProviderName;

    public Task<SubscriptionPlan[]> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        return billingProvider.GetSubscriptionPlans(cancellationToken);
    }

    public async Task<string> Purchase(IUiContext uiContext, PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        try {
            PurchaseState = BillingPurchaseState.Started;
            var providerOrderId = await billingProvider.Purchase(uiContext, purchaseParams, cancellationToken).Vhc();
            PurchaseState = BillingPurchaseState.Processing;
            await WaitForProcessProviderOrder(providerOrderId, cancellationToken).Vhc();
            return providerOrderId;
        }
        finally {
            PurchaseState = BillingPurchaseState.None;
        }
    }

    // Check order state 'isProcessed' for 6 time
    private async Task WaitForProcessProviderOrder(string providerOrderId, CancellationToken cancellationToken)
    {
        var httpClient = appAuthenticationProvider.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);

        for (var counter = 0; ; counter++) {
            try {
                var subscriptionOrder = await currentVpnUserClient
                    .GetSubscriptionOrderByProviderOrderIdAsync(storeAppId, providerOrderId, cancellationToken).Vhc();
                if (subscriptionOrder.IsProcessed)
                    return;
                throw new Exception("Order has not processed yet.");
            }
            catch (Exception ex) {
                // We might encounter a "not exist" exception. Therefore, we need to wait for Google to send the provider order to the Store.
                VhLogger.Instance.LogWarning(ex, ex.Message);
                if (counter == 5) throw;
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).Vhc();
            }
        }
    }

    public void Dispose()
    {
        billingProvider.Dispose();
    }
}
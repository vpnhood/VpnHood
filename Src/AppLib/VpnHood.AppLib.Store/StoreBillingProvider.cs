﻿using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.Store.Api;

namespace VpnHood.AppLib.Store;

internal class StoreBillingProvider(
    Guid storeAppId,
    IAppAuthenticationProvider appAuthenticationProvider,
    IAppBillingProvider billingProvider)
    : IAppBillingProvider
{
    public BillingPurchaseState PurchaseState { get; private set; }

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
            await WaitForProcessProviderOrder(providerOrderId).VhConfigureAwait();
            return providerOrderId;
        }
        finally {
            PurchaseState = BillingPurchaseState.None;
        }
    }

    // Check order state 'isProcessed' for 6 time
    private async Task WaitForProcessProviderOrder(string providerOrderId)
    {
        var httpClient = appAuthenticationProvider.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);

        for (var counter = 0;; counter++) {
            try {
                var subscriptionOrder = await currentVpnUserClient
                    .GetSubscriptionOrderByProviderOrderIdAsync(storeAppId, providerOrderId).VhConfigureAwait();
                if (subscriptionOrder.IsProcessed)
                    return;
                throw new Exception("Order has not processed yet.");
            }
            catch (Exception ex) {
                // We might encounter a �not exist� exception. Therefore, we need to wait for Google to send the provider order to the Store.
                VhLogger.Instance.LogWarning(ex, ex.Message);
                if (counter == 5) throw;
                await Task.Delay(TimeSpan.FromSeconds(5)).VhConfigureAwait();
            }
        }
    }

    public void Dispose()
    {
        billingProvider.Dispose();
    }
}
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Store.Api;

namespace VpnHood.AppLib.Store;

internal class StoreOrderProcessor(
    Guid storeAppId,
    IAppAuthenticationProvider authenticationProvider)
    : IAppOrderProcessor
{
    public Task<AppPurchaseAttribution> PreparePurchase(CancellationToken cancellationToken)
    {
        var userId = authenticationProvider.UserId
            ?? throw new AuthenticationException("Could not prepare the purchase because the user is not signed in.");

        return Task.FromResult(new AppPurchaseAttribution {
            AccountId = userId
        });
    }

    // Check order state 'isProcessed' for 6 time
    public async Task CompleteOrder(AppPurchaseResult purchaseResult, CancellationToken cancellationToken)
    {
        var httpClient = authenticationProvider.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);

        for (var counter = 0; ; counter++) {
            try {
                var subscriptionOrder = await currentVpnUserClient
                    .GetSubscriptionOrderByProviderOrderIdAsync(storeAppId, purchaseResult.ProviderOrderId,
                        cancellationToken).Vhc();
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
}

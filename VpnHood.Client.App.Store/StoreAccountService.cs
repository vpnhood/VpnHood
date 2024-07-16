using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Abstractions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Store.Api;

namespace VpnHood.Client.App.Store;

public class StoreAccountService : IAppAccountService, IDisposable
{
    private readonly Guid _storeAppId;
    public IAppAuthenticationService Authentication { get; }
    public IAppBillingService? Billing { get; }

    public StoreAccountService(IAppAuthenticationService authenticationService,
        IAppBillingService? billingService,
        Guid storeAppId)
    {
        _storeAppId = storeAppId;
        Authentication = authenticationService;
        Billing = billingService != null ? new StoreBillingService(this, billingService) : null;
    }

    public async Task<AppAccount?> GetAccount()
    {
        if (Authentication.UserId == null)
            return null;

        var httpClient = Authentication.HttpClient;
        var authenticationClient = new AuthenticationClient(httpClient);
        var currentUser = await authenticationClient.GetCurrentUserAsync().VhConfigureAwait();

        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var activeSubscription =
            await currentVpnUserClient.ListSubscriptionsAsync(_storeAppId, false, false).VhConfigureAwait();
        var subscriptionLastOrder = activeSubscription.SingleOrDefault()?.LastOrder;

        var appAccount = new AppAccount {
            UserId = currentUser.UserId,
            Name = currentUser.Name,
            Email = currentUser.Email,
            SubscriptionId = subscriptionLastOrder?.SubscriptionId.ToString(),
            ProviderPlanId = subscriptionLastOrder?.ProviderPlanId
        };

        return appAccount;
    }

    // Check order state 'isProcessed' for 6 time
    public async Task WaitForProcessProviderOrder(string providerOrderId)
    {
        var httpClient = Authentication.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);

        for (var counter = 0;; counter++) {
            try {
                var subscriptionOrder = await currentVpnUserClient
                    .GetSubscriptionOrderByProviderOrderIdAsync(_storeAppId, providerOrderId).VhConfigureAwait();
                if (subscriptionOrder.IsProcessed)
                    return;
                throw new Exception("Order has not processed yet.");
            }
            catch (Exception ex) {
                // We might encounter a ‘not exist’ exception. Therefore, we need to wait for Google to send the provider order to the Store.
                VhLogger.Instance.LogWarning(ex, ex.Message);
                if (counter == 5) throw;
                await Task.Delay(TimeSpan.FromSeconds(5)).VhConfigureAwait();
            }
        }
    }

    public async Task<string[]> GetAccessKeys(string subscriptionId)
    {
        var httpClient = Authentication.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var accessTokens = await currentVpnUserClient
            .ListAccessTokensAsync(_storeAppId, subscriptionId: Guid.Parse(subscriptionId)).VhConfigureAwait();

        var accessKeyList = new List<string>();
        foreach (var accessToken in accessTokens) {
            var accessKey = await currentVpnUserClient.GetAccessKeyAsync(_storeAppId, accessToken.AccessTokenId)
                .VhConfigureAwait();
            accessKeyList.Add(accessKey);
        }

        return accessKeyList.ToArray();
    }

    public void Dispose()
    {
        Billing?.Dispose();
        Authentication.Dispose();
    }
}
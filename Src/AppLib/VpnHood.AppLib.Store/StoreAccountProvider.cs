using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Common.Utils;
using VpnHood.Store.Api;

namespace VpnHood.AppLib.Store;

public class StoreAccountProvider(
    IAppAuthenticationProvider authenticationProvider,
    IAppBillingProvider? billingProvider,
    Guid storeAppId)
    : IAppAccountProvider, IDisposable
{
    public IAppAuthenticationProvider AuthenticationProvider { get; } = authenticationProvider;

    public IAppBillingProvider? BillingProvider { get; } = billingProvider != null
        ? new StoreBillingProvider(storeAppId, authenticationProvider, billingProvider)
        : null;

    public async Task<AppAccount?> GetAccount()
    {
        if (AuthenticationProvider.UserId == null)
            return null;

        var httpClient = AuthenticationProvider.HttpClient;
        var authenticationClient = new AuthenticationClient(httpClient);
        var currentUser = await authenticationClient.GetCurrentUserAsync().VhConfigureAwait();

        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var activeSubscription =
            await currentVpnUserClient.ListSubscriptionsAsync(storeAppId, false, false).VhConfigureAwait();
        var subscriptionLastOrder = activeSubscription.SingleOrDefault()?.LastOrder;

        var appAccount = new AppAccount {
            UserId = currentUser.UserId,
            Name = currentUser.Name,
            Email = currentUser.Email,
            SubscriptionId = subscriptionLastOrder?.SubscriptionId.ToString(),
            ProviderPlanId = subscriptionLastOrder?.ProviderPlanId,
            CreatedTime = subscriptionLastOrder?.CreatedTime,
            ExpirationTime = subscriptionLastOrder?.ExpirationTime,
            PriceAmount = subscriptionLastOrder?.PriceAmount,
            PriceCurrency = subscriptionLastOrder?.PriceCurrency,
            IsAutoRenew = subscriptionLastOrder?.IsAutoRenew,
            ProviderSubscriptionId = subscriptionLastOrder?.ProviderSubscriptionId
        };

        return appAccount;
    }

    public async Task<string[]> ListAccessKeys(string subscriptionId)
    {
        var httpClient = AuthenticationProvider.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var accessTokens = await currentVpnUserClient
            .ListAccessTokensAsync(storeAppId, subscriptionId: Guid.Parse(subscriptionId)).VhConfigureAwait();

        var accessKeyList = new List<string>();
        foreach (var accessToken in accessTokens) {
            var accessKey = await currentVpnUserClient.GetAccessKeyAsync(storeAppId, accessToken.AccessTokenId)
                .VhConfigureAwait();
            accessKeyList.Add(accessKey);
        }

        return accessKeyList.ToArray();
    }

    public void Dispose()
    {
        BillingProvider?.Dispose();
        AuthenticationProvider.Dispose();
    }
}
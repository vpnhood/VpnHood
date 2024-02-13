using VpnHood.Client.App.Abstractions;
using VpnHood.Store.Api;

namespace VpnHood.Client.App.Store;

public class AppAccountService(
    IAppAuthenticationService authenticationService, 
    IAppBillingService? billingService,
    Guid storeAppId) 
    : IAppAccountService, IDisposable
{
    public IAppAuthenticationService Authentication => authenticationService;
    public IAppBillingService? Billing => billingService;

    public async Task<AppAccount?> GetAccount()
    {
        if (authenticationService.UserId == null)
            return null;

        var httpClient = authenticationService.HttpClient;
        var authenticationClient = new AuthenticationClient(httpClient);
        var currentUser = await authenticationClient.GetCurrentUserAsync();

        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var activeSubscription = await currentVpnUserClient.ListSubscriptionsAsync(storeAppId, false, false);
        var subscriptionPlanId = activeSubscription.SingleOrDefault()?.LastOrder;

        var appAccount = new AppAccount()
        {
            UserId = currentUser.UserId,
            Name = currentUser.Name,
            Email = currentUser.Email,
            SubscriptionPlanId = subscriptionPlanId?.ProviderPlanId,
        };
        return appAccount;
    }

    public async Task<AppSubscriptionOrder> GetSubscriptionOrderByProviderOrderId(string providerOrderId)
    {
        var httpClient = authenticationService.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var subscriptionOrder = await currentVpnUserClient.GetSubscriptionOrderByProviderOrderIdAsync(storeAppId, providerOrderId);
        var appSubscriptionOrder = new AppSubscriptionOrder()
        {
            SubscriptionId = subscriptionOrder.SubscriptionId,
            ProviderPlanId = subscriptionOrder.ProviderPlanId,
            IsProcessed = subscriptionOrder.IsProcessed,
        };
        return appSubscriptionOrder;
    }

    public async Task<List<string>> GetAccessKeys(string subscriptionId)
    {
        var httpClient = authenticationService.HttpClient;
        var subscriptionsClient = new SubscriptionsClient(httpClient);
        var subscriptionData = await subscriptionsClient.GetAsync(storeAppId, Guid.Parse(subscriptionId), true);
        var subscriptionAccessTokens = subscriptionData.AccessTokens;
        if (subscriptionAccessTokens == null)
            throw new Exception("The subscription does not have any AccessToken.");

        var accessKeyList = new List<string>();
        var accessTokensClient = new AccessTokensClient(httpClient);
        foreach (var accessToken in subscriptionAccessTokens)
        {
            var accessKey = await accessTokensClient.GetAccessKeyAsync(storeAppId, accessToken.AccessTokenId);
            accessKeyList.Add(accessKey);
        }

        return accessKeyList;
    }

    public void Dispose()
    {
        Billing?.Dispose();
        Authentication.Dispose();
    }
}
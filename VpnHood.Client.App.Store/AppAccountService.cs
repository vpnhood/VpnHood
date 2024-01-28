using VpnHood.Client.App.Abstractions;
using VpnHood.Store.Api;

namespace VpnHood.Client.App.Store;

public class AppAccountService(
    AppAuthenticationService authenticationService, 
    IAppBillingService? billingService) 
    : IAppAccountService, IDisposable
{
    public IAppAuthenticationService Authentication => authenticationService;
    public IAppBillingService? Billing => billingService;

    public async Task<AppAccount> GetAccount()
    {
        var httpClient = authenticationService.HttpClient;
        var authenticationClient = new AuthenticationClient(httpClient);
        var currentUser = await authenticationClient.GetCurrentUserAsync();

        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var activeSubscription = await currentVpnUserClient.ListSubscriptionsAsync(authenticationService.StoreAppId, false, false);
        var subscriptionPlanId = activeSubscription.SingleOrDefault()?.LastOrder;

        var appAccount = new AppAccount()
        {
            Email = currentUser.Email,
            Name = currentUser.Name,
            SubscriptionPlanId = subscriptionPlanId?.ProviderPlanId,
        };
        return appAccount;
    }

    public void Dispose()
    {
        Billing?.Dispose();
        Authentication.Dispose();
    }
}
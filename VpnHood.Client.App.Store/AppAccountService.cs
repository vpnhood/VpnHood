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

    public void Dispose()
    {
        Billing?.Dispose();
        Authentication.Dispose();
    }
}
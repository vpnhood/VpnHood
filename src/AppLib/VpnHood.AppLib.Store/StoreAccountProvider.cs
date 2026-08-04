using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Store.Api;

namespace VpnHood.AppLib.Store;

public class StoreAccountProvider(
    IAppAuthenticationProvider authenticationProvider,
    IAppBillingProvider? billingProvider,
    Guid storeAppId)
    : IAppAccountProvider, IDisposable
{
    public IAppAuthenticationProvider AuthenticationProvider { get; } = authenticationProvider;

    public AppBilling? Billing { get; } = billingProvider != null
        ? new AppBilling {
            Provider = billingProvider,
            OrderProcessor = new StoreOrderProcessor(storeAppId, authenticationProvider)
        }
        : null;

    public async Task<AppAccount?> GetAccount(CancellationToken cancellationToken)
    {
        if (AuthenticationProvider.UserId == null)
            return null;

        var httpClient = AuthenticationProvider.HttpClient;
        var authenticationClient = new AuthenticationClient(httpClient);
        var currentUser = await authenticationClient.GetCurrentUserAsync(cancellationToken).Vhc();

        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var activeSubscription =
            await currentVpnUserClient.ListSubscriptionsAsync(storeAppId, false, false, cancellationToken).Vhc();
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

    public async Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken)
    {
        var httpClient = AuthenticationProvider.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var accessTokens = await currentVpnUserClient
            .ListAccessTokensAsync(storeAppId, subscriptionId: Guid.Parse(subscriptionId),
                cancellationToken: cancellationToken).Vhc();

        var accessKeyList = new List<string>();
        foreach (var accessToken in accessTokens) {
            var accessKey = await currentVpnUserClient.GetAccessKeyAsync(
                storeAppId, accessToken.AccessTokenId,
                cancellationToken).Vhc();
            accessKeyList.Add(accessKey);
        }

        return accessKeyList;
    }

    public async Task<string> GetAccessCode(string subscriptionId, CancellationToken cancellationToken)
    {
        var httpClient = AuthenticationProvider.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var accessTokens = await currentVpnUserClient
            .ListAccessTokensAsync(storeAppId, subscriptionId: Guid.Parse(subscriptionId),
                cancellationToken: cancellationToken).Vhc();

        var lastAccessToken = accessTokens.Last();
        var accessCode = await currentVpnUserClient.GetAccessCodeAsync(storeAppId, lastAccessToken.AccessTokenId, cancellationToken).Vhc();
        return accessCode;
    }

    public void Dispose()
    {
        Billing?.Provider.Dispose();
        AuthenticationProvider.Dispose();
    }
}
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
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
    private AppAccount? _appAccount;
    
    private static string AppAccountFilePath => Path.Combine(VpnHoodApp.Instance.AppDataFolderPath, "account", "account.json");

    public async Task<AppAccount> SignInWithGoogle()
    {
        await authenticationService.SignInWithGoogle();
        var appAccount = await GetAccount();
        return appAccount ?? throw new ArgumentNullException(nameof(appAccount));
    }

    public async Task<AppAccount?> GetAccount()
    {
        if (authenticationService.UserId == null)
            return null;

        // Get from local cache
        _appAccount ??= VhUtil.JsonDeserializeFile<AppAccount>(AppAccountFilePath, logger: VhLogger.Instance);
        if (_appAccount != null)
            return _appAccount;

        // Update cache from server and update local cache
        _appAccount = await GetAccountFromServer();
        Directory.CreateDirectory(Path.GetDirectoryName(AppAccountFilePath)!);
        await File.WriteAllTextAsync(AppAccountFilePath, JsonSerializer.Serialize(_appAccount));

        return _appAccount;
    }

    public void Refresh()
    {
        _appAccount = null;
    }

    private async Task<AppAccount> GetAccountFromServer()
    {
        var httpClient = authenticationService.HttpClient;
        var authenticationClient = new AuthenticationClient(httpClient);
        var currentUser = await authenticationClient.GetCurrentUserAsync();

        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var activeSubscription = await currentVpnUserClient.ListSubscriptionsAsync(storeAppId, false, false);
        var subscriptionLastOrder = activeSubscription.SingleOrDefault()?.LastOrder;

        var appAccount = new AppAccount
        {
            UserId = currentUser.UserId,
            Name = currentUser.Name,
            Email = currentUser.Email,
            SubscriptionId = subscriptionLastOrder?.SubscriptionId.ToString(),
            ProviderPlanId = subscriptionLastOrder?.ProviderPlanId
        };

        // Get access keys from user account
        var accessKeys = _appAccount?.SubscriptionId != null
            ? await GetAccessKeys(_appAccount.SubscriptionId)
            : [];
        VpnHoodApp.Instance.ClientProfileService.UpdateFromAccount(accessKeys.ToArray());

        return appAccount;
    }

    // Check order state 'isProcessed' for 6 time
    public async Task<bool> IsSubscriptionOrderProcessed(string providerOrderId)
    {
        var httpClient = authenticationService.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);

        for (var counter = 0; counter < 5; counter++)
        {
            try
            {
                var subscriptionOrder = await currentVpnUserClient.GetSubscriptionOrderByProviderOrderIdAsync(storeAppId, providerOrderId);
                if (subscriptionOrder.IsProcessed == false)
                    throw new Exception("Order has not processed yet.");

                // Order process complete
                return subscriptionOrder.IsProcessed;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning(ex, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
        return false;
    }

    public async Task<List<string>> GetAccessKeys(string subscriptionId)
    {
        var httpClient = authenticationService.HttpClient;
        var currentVpnUserClient = new CurrentVpnUserClient(httpClient);
        var accessTokens = await currentVpnUserClient.ListAccessTokensAsync(storeAppId, subscriptionId: Guid.Parse(subscriptionId));

        var accessKeyList = new List<string>();
        foreach (var accessToken in accessTokens)
        {
            var accessKey = await currentVpnUserClient.GetAccessKeyAsync(storeAppId, accessToken.AccessTokenId);
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
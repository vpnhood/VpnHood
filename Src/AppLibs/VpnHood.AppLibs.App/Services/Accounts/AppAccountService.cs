using System.Text.Json;
using VpnHood.AppLibs.Abstractions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLibs.Services.Accounts;

public class AppAccountService
{
    private AppAccount? _appAccount;
    private readonly VpnHoodApp _vpnHoodApp;
    private readonly IAppAccountProvider _accountProvider;

    public AppAccountService(VpnHoodApp vpnHoodApp, IAppAccountProvider accountProvider)
    {
        _vpnHoodApp = vpnHoodApp;
        _accountProvider = accountProvider;
        AuthenticationService = new AppAuthenticationService(this, accountProvider.AuthenticationProvider);
        BillingService = accountProvider.BillingProvider != null ? new AppBillingService(this, accountProvider.BillingProvider) : null;
    }

    private string AppAccountFilePath => Path.Combine(_vpnHoodApp.StorageFolderPath, "account", "account.json");
    
    public AppAuthenticationService AuthenticationService { get; }

    public AppBillingService? BillingService { get; }

    public async Task<AppAccount?> GetAccount()
    {
        if (AuthenticationService.UserId == null)
            return null;

        // Get from local cache
        _appAccount ??= VhUtil.JsonDeserializeFile<AppAccount>(AppAccountFilePath, logger: VhLogger.Instance);
        if (_appAccount != null)
            return _appAccount;

        // Update cache from server and update local cache
        _appAccount = await _accountProvider.GetAccount().VhConfigureAwait();
        Directory.CreateDirectory(Path.GetDirectoryName(AppAccountFilePath)!);
        await File.WriteAllTextAsync(AppAccountFilePath, JsonSerializer.Serialize(_appAccount)).VhConfigureAwait();

        return _appAccount;
    }

    public async Task Refresh(bool updateCurrentClientProfile = false)
    {
        // clear cache
        ClearCache();

        // get access tokens from account
        var account = await GetAccount().VhConfigureAwait();
        var accessKeys = account?.SubscriptionId != null
            ? await GetAccessKeys(account.SubscriptionId).VhConfigureAwait()
            : [];

        // update profiles
        await _vpnHoodApp.RefreshAccount(accessKeys, updateCurrentClientProfile);
    }

    internal void ClearCache()
    {
        File.Delete(AppAccountFilePath);
        _appAccount = null;
    }

    public Task<string[]> GetAccessKeys(string subscriptionId)
    {
        return _accountProvider.GetAccessKeys(subscriptionId);
    }
}
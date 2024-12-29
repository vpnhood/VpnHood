using System.Text.Json;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.Services.Accounts;

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

    public Task<AppAccount?> GetAccount()
    {
        return GetAccount(useCache: true);
    }

    private async Task<AppAccount?> GetAccount(bool useCache)
    {
        if (AuthenticationService.UserId == null) {
            ClearCache();
            return null;
        }

        // Get from local cache
        if (useCache) {
            _appAccount ??= VhUtil.JsonDeserializeFile<AppAccount>(AppAccountFilePath, logger: VhLogger.Instance);
            if (_appAccount != null && (_appAccount.ExpirationTime == null || _appAccount.ExpirationTime > DateTime.UtcNow))
                return _appAccount;
        }

        // Update cache from server and update local cache
        _appAccount = await _accountProvider.GetAccount().VhConfigureAwait();
        Directory.CreateDirectory(Path.GetDirectoryName(AppAccountFilePath)!);
        await File.WriteAllTextAsync(AppAccountFilePath, JsonSerializer.Serialize(_appAccount)).VhConfigureAwait();

        return _appAccount;
    }

    public async Task Refresh(bool updateCurrentClientProfile = false)
    {
        // get access tokens from account
        var account = await GetAccount(false).VhConfigureAwait();
        var accessKeys = account?.SubscriptionId != null
            ? await ListAccessKeys(account.SubscriptionId).VhConfigureAwait()
            : [];

        // update profiles
        _vpnHoodApp.ClientProfileService.UpdateFromAccount(accessKeys);
        _vpnHoodApp.ValidateAccountClientProfiles(updateCurrentClientProfile);
    }

    private void ClearCache()
    {
        if (File.Exists(AppAccountFilePath))
            File.Delete(AppAccountFilePath);
        _appAccount = null;
    }

    public Task<string[]> ListAccessKeys(string subscriptionId)
    {
        return _accountProvider.ListAccessKeys(subscriptionId);
    }
}
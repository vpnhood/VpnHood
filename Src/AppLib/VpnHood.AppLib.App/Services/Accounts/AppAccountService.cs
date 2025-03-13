using System.Text.Json;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

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
        BillingService = accountProvider.BillingProvider != null
            ? new AppBillingService(this, accountProvider.BillingProvider)
            : null;
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
            ClearAccount();
            return null;
        }

        // Get from local cache
        if (useCache) {
            _appAccount ??= JsonUtils.TryDeserializeFile<AppAccount>(AppAccountFilePath, logger: VhLogger.Instance);
            if (_appAccount != null)
                return _appAccount;
        }

        // Update cache from server and update local cache
        await Refresh(true);
        return _appAccount;
    }


    public async Task Refresh(bool updateCurrentClientProfile = false)
    {
        _appAccount = await _accountProvider.GetAccount().VhConfigureAwait();
        Directory.CreateDirectory(Path.GetDirectoryName(AppAccountFilePath)!);
        await File.WriteAllTextAsync(AppAccountFilePath, JsonSerializer.Serialize(_appAccount)).VhConfigureAwait();

        // update profiles
        var accessKeys = _appAccount?.SubscriptionId != null
            ? await ListAccessKeys(_appAccount.SubscriptionId).VhConfigureAwait()
            : [];

        // update profiles
        _vpnHoodApp.ClientProfileService.UpdateFromAccount(accessKeys);
        _vpnHoodApp.ValidateAccountClientProfiles(updateCurrentClientProfile);
    }

    private void ClearAccount()
    {
        if (File.Exists(AppAccountFilePath))
            File.Delete(AppAccountFilePath);

        _appAccount = null;
        _vpnHoodApp.ClientProfileService.UpdateFromAccount([]);
        _vpnHoodApp.ValidateAccountClientProfiles(false);
    }

    public Task<string[]> ListAccessKeys(string subscriptionId)
    {
        return _accountProvider.ListAccessKeys(subscriptionId);
    }
}
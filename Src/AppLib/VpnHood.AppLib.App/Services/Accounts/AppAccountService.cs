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

    public bool IsPremium => _vpnHoodApp.CurrentClientProfileInfo?.IsPremiumAccount == true;

    public AppAuthenticationService AuthenticationService { get; }

    public AppBillingService? BillingService { get; }

    public Task<AppAccount?> GetAccount(CancellationToken cancellationToken)
    {
        return GetAccount(useCache: true, cancellationToken);
    }

    private async Task<AppAccount?> GetAccount(bool useCache, CancellationToken cancellationToken)
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
        await Refresh(true, cancellationToken);
        return _appAccount;
    }

    public Task Refresh(CancellationToken cancellationToken)
    {
        return Refresh(updateCurrentClientProfile: false, cancellationToken);
    }

    public async Task Refresh(bool updateCurrentClientProfile, CancellationToken cancellationToken)
    {
        _appAccount = await _accountProvider.GetAccount(cancellationToken).Vhc();
        Directory.CreateDirectory(Path.GetDirectoryName(AppAccountFilePath)!);
        await File.WriteAllTextAsync(AppAccountFilePath, JsonSerializer.Serialize(_appAccount), cancellationToken).Vhc();

        // update profiles
        var accessKeys = _appAccount?.SubscriptionId != null
            ? await ListAccessKeys(_appAccount.SubscriptionId, cancellationToken).Vhc()
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

    public Task<string[]> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken = default)
    {
        return _accountProvider.ListAccessKeys(subscriptionId, cancellationToken);
    }
}
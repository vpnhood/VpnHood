using System.Text.Json;
using VpnHood.Client.App.Abstractions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Services;

public class AppAccountService(VpnHoodApp vpnHoodApp, IAppAccountProvider accountProvider) {
    private AppAccount? _appAccount;
    private string AppAccountFilePath => Path.Combine(vpnHoodApp.StorageFolderPath, "account", "account.json");

    public AppAuthenticationService Authentication { get; } = new(vpnHoodApp, accountProvider.AuthenticationProvider);

    public AppBillingService? BillingService { get; } = accountProvider.BillingProvider != null
        ? new AppBillingService(vpnHoodApp, accountProvider.BillingProvider) : null;

    public async Task<AppAccount?> GetAccount()
    {
        if (Authentication.UserId == null)
            return null;

        // Get from local cache
        _appAccount ??= VhUtil.JsonDeserializeFile<AppAccount>(AppAccountFilePath, logger: VhLogger.Instance);
        if (_appAccount != null)
            return _appAccount;

        // Update cache from server and update local cache
        _appAccount = await accountProvider.GetAccount().VhConfigureAwait();
        Directory.CreateDirectory(Path.GetDirectoryName(AppAccountFilePath)!);
        await File.WriteAllTextAsync(AppAccountFilePath, JsonSerializer.Serialize(_appAccount)).VhConfigureAwait();

        return _appAccount;
    }

    internal void ClearCache()
    {
        File.Delete(AppAccountFilePath);
        _appAccount = null;
    }

    public Task<string[]> GetAccessKeys(string subscriptionId)
    {
        return accountProvider.GetAccessKeys(subscriptionId);
    }
}
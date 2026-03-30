using System.Text.Json;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
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
        if (_appAccount is { SubscriptionId: not null }) {
            var accessCode = await _accountProvider.GetAccessCode(_appAccount.SubscriptionId, cancellationToken);

            // update profiles
            var currentProfile = GetCurrentProfile();
            if (currentProfile != null) {
                currentProfile.AccessCode = accessCode;
                _vpnHoodApp.ClientProfileService.Update(currentProfile.ClientProfileId,
                    new ClientProfileUpdateParams {
                        AccessCode = accessCode,
                        IsAccessCodeFromAccount = true
                    });
            }
        }

        // if updateCurrentClientProfile is true, it means we want to validate the current profile with the new account info,
        // which may cause the current profile to be switched if it's no longer valid. If it's false, we just want to update
        // the access code for the current profile without switching it, even if it's no longer valid.
        _vpnHoodApp.ValidateAccountClientProfiles(updateCurrentClientProfile);
    }

    private void ClearAccount()
    {
        if (File.Exists(AppAccountFilePath))
            File.Delete(AppAccountFilePath);

        _appAccount = null;

        // update profiles - only clear access code if it was set from the account
        var currentProfile = GetCurrentProfile();
        if (currentProfile != null) {
            if (!string.IsNullOrEmpty(currentProfile.AccessCode) && currentProfile.IsAccessCodeFromAccount) {
                _vpnHoodApp.ClientProfileService.Update(currentProfile.ClientProfileId,
                    new ClientProfileUpdateParams {
                        AccessCode = null,
                        IsAccessCodeFromAccount = false
                    });
            }
        }

        _vpnHoodApp.ValidateAccountClientProfiles(false);
    }

    private ClientProfile? GetCurrentProfile()
    {
        var profileId = _vpnHoodApp.CurrentClientProfileInfo?.ClientProfileId;
        return profileId != null
            ? _vpnHoodApp.ClientProfileService.FindById(profileId.Value)
            : null;
    }

    public Task<string[]> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken = default)
    {
        return _accountProvider.ListAccessKeys(subscriptionId, cancellationToken);
    }
}
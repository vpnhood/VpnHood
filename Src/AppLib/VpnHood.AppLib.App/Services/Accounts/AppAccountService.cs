using Microsoft.Extensions.Logging;
using System.Text.Json;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Accounts;

public class AppAccountService
{
    private AppAccount? _appAccount;
    private readonly AppSettingsService _settingsService;
    private readonly IAppAccountProvider _accountProvider;
    private readonly ClientProfileService _clientProfileService;
    private readonly string _appAccountFilePath;

    public AppAccountService(
        AppSettingsService settingsService,
        IAppAccountProvider accountProvider,
        ClientProfileService clientProfileService,
        string storageFolderPath)
    {
        _settingsService = settingsService;
        _accountProvider = accountProvider;
        _clientProfileService = clientProfileService;
        _appAccountFilePath = Path.Combine(storageFolderPath, "account.json");
        AuthenticationService = new AppAuthenticationService(this, accountProvider.AuthenticationProvider);
        BillingService = accountProvider.BillingProvider != null
            ? new AppBillingService(this, accountProvider.BillingProvider)
            : null;
    }


    public async Task<bool> IsPremium(CancellationToken cancellationToken)
    {
        var account = await GetAccount(cancellationToken).Vhc();
        return !string.IsNullOrEmpty(account?.SubscriptionId);
    }

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
            _appAccount ??= JsonUtils.TryDeserializeFile<AppAccount>(_appAccountFilePath, logger: VhLogger.Instance);
            if (_appAccount != null)
                return _appAccount;
        }

        // Update cache from server and update local cache
        await Refresh(cancellationToken);
        return _appAccount;
    }

    public async Task Refresh(CancellationToken cancellationToken)
    {
        _appAccount = await _accountProvider.GetAccount(cancellationToken).Vhc();
        Directory.CreateDirectory(Path.GetDirectoryName(_appAccountFilePath)!);
        await File.WriteAllTextAsync(_appAccountFilePath, JsonSerializer.Serialize(_appAccount), cancellationToken).Vhc();

        // if requested, update the current client profile with the new access code from the account
        var currentProfile = GetCurrentProfile();
        if (currentProfile is null)
            throw new InvalidOperationException("Could not refresh account when there is no current client profile.");

        // update profiles
        var accessCode = _appAccount is { SubscriptionId: not null }
            ? await _accountProvider.GetAccessCode(_appAccount.SubscriptionId, cancellationToken)
            : null;

        // override profiles if access code is from account, or if there is an access code from account to set (e.g. first time login or access code changed)
        if (currentProfile.IsAccessCodeFromAccount) {
            _clientProfileService.Update(currentProfile.ClientProfileId,
                new ClientProfileUpdateParams {
                    AccessCode = accessCode,
                    IsAccessCodeFromAccount = !string.IsNullOrEmpty(accessCode)
                });
            return;
        }

        // Only update if there is an access code from account to set (don't clear it)
        if (!string.IsNullOrEmpty(accessCode)) {
            _clientProfileService.Update(currentProfile.ClientProfileId,
                new ClientProfileUpdateParams {
                    AccessCode = accessCode,
                    IsAccessCodeFromAccount = false
                });
        }
    }

    private void ClearAccount()
    {
        if (File.Exists(_appAccountFilePath))
            File.Delete(_appAccountFilePath);

        _appAccount = null;

        // update profiles - only clear access code if it was set from the account
        var currentProfile = GetCurrentProfile();
        if (currentProfile is null)
            return;

        // remove access code if it is from account (not custom access code)
        if (!string.IsNullOrEmpty(currentProfile.AccessCode) && currentProfile.IsAccessCodeFromAccount) {
            _clientProfileService.Update(currentProfile.ClientProfileId,
                new ClientProfileUpdateParams {
                    AccessCode = null,
                    IsAccessCodeFromAccount = false
                });
        }
    }

    private ClientProfile? GetCurrentProfile()
    {
        var profileId = _settingsService.UserSettings.ClientProfileId;
        var profile = _clientProfileService.FindById(profileId ?? Guid.Empty) 
            ?? _clientProfileService.List().FirstOrDefault();
        return profile;
    }

    public Task<string[]> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken = default)
    {
        return _accountProvider.ListAccessKeys(subscriptionId, cancellationToken);
    }
}
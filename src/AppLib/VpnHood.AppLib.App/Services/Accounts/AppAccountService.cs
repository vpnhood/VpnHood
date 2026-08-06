using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Accounts;

public class AppAccountService
{
    // A cached account is only trusted while its own expiry lies in the future.
    // After the store renews a subscription, the new expiry exists only on the
    // server — serving the stale cache forever would show "expired" and offer a
    // re-purchase the store rejects. Genuinely expired accounts re-check at most
    // once per this interval, so the server is not hammered.
    private static readonly TimeSpan ExpiredAccountRecheckInterval = TimeSpan.FromMinutes(5);

    private AppAccount? _appAccount;
    private DateTime _lastRefreshAttemptTime = DateTime.MinValue;
    private readonly AppSettingsService _settingsService;
    private readonly IAppAccountProvider _accountProvider;
    private readonly ClientProfileService _clientProfileService;
    private readonly string _storageFolderPath;
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
        _storageFolderPath = storageFolderPath;
        _appAccountFilePath = Path.Combine(storageFolderPath, "account.json");
        AuthenticationService = new AppAuthenticationService(this, accountProvider.AuthenticationProvider);
        BillingService = accountProvider.Billing != null
            ? new AppBillingService(this, accountProvider.Billing)
            : null;
    }


    public async Task<bool> IsPremium(bool useCache, CancellationToken cancellationToken)
    {
        var account = await GetAccount(useCache, cancellationToken).Vhc();
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
            if (_appAccount != null && (IsCacheCurrent(_appAccount) || !IsRecheckDue()))
                return _appAccount;
        }

        // Update cache from server and update local cache. If the server is
        // unreachable, a stale account is still better than none for display.
        try {
            await Refresh(cancellationToken);
        }
        catch (Exception ex) when (_appAccount != null) {
            VhLogger.Instance.LogWarning(ex, "Could not refresh the account. Using the cached one.");
        }
        return _appAccount;
    }

    // the cached account is current while its own expiry (if any) has not passed
    private static bool IsCacheCurrent(AppAccount account)
    {
        return account.ExpirationTime == null || account.ExpirationTime.Value.ToUniversalTime() > DateTime.UtcNow;
    }

    private bool IsRecheckDue()
    {
        return DateTime.UtcNow - _lastRefreshAttemptTime >= ExpiredAccountRecheckInterval;
    }

    public async Task Refresh(CancellationToken cancellationToken)
    {
        _lastRefreshAttemptTime = DateTime.UtcNow;
        _appAccount = await _accountProvider.GetAccount(cancellationToken).Vhc();
        Directory.CreateDirectory(_storageFolderPath);
        await File.WriteAllTextAsync(_appAccountFilePath, JsonSerializer.Serialize(_appAccount), cancellationToken).Vhc();

        // if requested, update the current client profile with the new access code from the account
        var currentProfile = GetCurrentProfile();
        if (currentProfile is null)
            throw new InvalidOperationException("Could not refresh account when there is no current client profile.");

        // remove AccessCode from Account if there is no account (signed out)
        // keep it if account exists but there is no subscription or access code is empty, because user have to remove it manually
        if (_appAccount is null) {
            if (currentProfile.IsAccessCodeFromAccount)
                _clientProfileService.Update(currentProfile.ClientProfileId,
                    new ClientProfileUpdateParams {
                        AccessCode = new Patch<string?>(null),
                        IsAccessCodeFromAccount = false
                    });
            return;
        }

        // get access code from account
        var accessCode = _appAccount.SubscriptionId is not null
            ? await _accountProvider.GetAccessCode(_appAccount.SubscriptionId, cancellationToken)
            : null;

        if (string.IsNullOrEmpty(accessCode))
            return;

        // override profiles if access code is from account, or if there is an access code from account to set (e.g. first time login or access code changed)
        _clientProfileService.Update(currentProfile.ClientProfileId,
            new ClientProfileUpdateParams {
                AccessCode = new Patch<string?>(accessCode),
                IsAccessCodeFromAccount = true
            });
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
                    AccessCode = new Patch<string?>(null),
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

    public Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken)
    {
        return _accountProvider.ListAccessKeys(subscriptionId, cancellationToken);
    }
}
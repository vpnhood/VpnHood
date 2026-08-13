using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Devices.UiContexts;
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

    private readonly AsyncLock _refreshLock = new();
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
            // A rejected session is not an outage. The authentication provider has already dropped
            // it, so the account held here belongs to someone the server no longer knows — serving
            // the cache is what used to keep a deleted person on screen for good.
            if (AuthenticationService.UserId == null) {
                VhLogger.Instance.LogInformation(ex,
                    "The account session is no longer valid. Forgetting the account on this device.");
                ClearAccount();
                return null;
            }

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

    /// <summary>
    /// "Forget me": the backend erases the person everywhere, then this device forgets the account —
    /// premium included. The refresh below is what strips the account-sourced access code, because
    /// an account-sourced code exists only while its account does.
    /// <para>
    /// The paid entitlement itself is not destroyed: the store still owns that subscription, and it
    /// can be brought back with Restore Purchase onto a new account. What ends here is this device
    /// serving premium off an account that no longer exists.
    /// </para>
    /// </summary>
    public async Task DeleteAccount(IUiContext uiContext, CancellationToken cancellationToken)
    {
        await _accountProvider.DeleteAccount(uiContext, cancellationToken).Vhc();
        await Refresh(cancellationToken).Vhc();
    }

    public async Task Refresh(CancellationToken cancellationToken)
    {
        // Serialized: this writes account.json and rewrites the current profile, and it is now
        // reached from the background at startup as well as from the UI. Two at once collide on the
        // file itself — the second writer finds it still open — and can leave the profile carrying
        // the loser's access code.
        using var refreshLock = await _refreshLock.LockAsync(cancellationToken).Vhc();

        _lastRefreshAttemptTime = DateTime.UtcNow;
        _appAccount = await _accountProvider.GetAccount(cancellationToken).Vhc();
        Directory.CreateDirectory(_storageFolderPath);
        await File.WriteAllTextAsync(_appAccountFilePath, JsonSerializer.Serialize(_appAccount), cancellationToken).Vhc();

        // if requested, update the current client profile with the new access code from the account
        var currentProfile = GetCurrentProfile();
        if (currentProfile is null)
            throw new InvalidOperationException("Could not refresh account when there is no current client profile.");

        // No account: signed out, deleted, or a session the portal no longer honours. An
        // account-sourced code lives and dies with its account, so it goes — see
        // RemoveAccountAccessCode.
        if (_appAccount is null) {
            RemoveAccountAccessCode();
            return;
        }

        // get access code from account
        var accessCode = _appAccount.SubscriptionId is not null
            ? await _accountProvider.GetAccessCode(_appAccount.SubscriptionId, cancellationToken)
            : null;

        // The subscription is over, so the code it delivered is spent. Leaving it on the profile
        // holds every LOCAL premium gate open — ClientProfile.IsPremium is true whenever an access
        // code is set — while the server has already stopped honouring it, which is the app
        // promising premium and then failing to connect. A code the user typed in stays untouched.
        if (string.IsNullOrEmpty(accessCode)) {
            RemoveAccountAccessCode();
            return;
        }

        // override profiles if access code is from account, or if there is an access code from account to set (e.g. first time login or access code changed)
        _clientProfileService.Update(currentProfile.ClientProfileId,
            new ClientProfileUpdateParams {
                AccessCode = new Patch<string?>(accessCode),
                IsAccessCodeFromAccount = true
            });
    }

    /// <summary>Forget the account on this device, premium included.</summary>
    private void ClearAccount()
    {
        if (File.Exists(_appAccountFilePath))
            File.Delete(_appAccountFilePath);

        _appAccount = null;
        RemoveAccountAccessCode();
    }

    /// <summary>
    /// Take the account-sourced access code away with the account. One rule, whatever made the
    /// account go: signed out, deleted here, deleted on another device, a session the portal no
    /// longer honours, or a subscription that ended. The code is the account's, not the device's —
    /// leaving it behind would keep serving premium off an account that no longer exists, and would
    /// carry paid access into whatever account signs in next.
    /// <para>
    /// The entitlement itself is not destroyed: the store still owns that subscription, so Restore
    /// Purchase brings it back onto a new account. A code the user typed in themselves is never
    /// touched.
    /// </para>
    /// </summary>
    private void RemoveAccountAccessCode()
    {
        var currentProfile = GetCurrentProfile();
        if (currentProfile is { IsAccessCodeFromAccount: true })
            _clientProfileService.Update(currentProfile.ClientProfileId,
                new ClientProfileUpdateParams {
                    AccessCode = new Patch<string?>(null),
                    IsAccessCodeFromAccount = false
                });
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

    /// <summary>The store product ids this app may sell — see <see cref="IAppAccountProvider.GetProductIds" />.</summary>
    public Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken)
    {
        return _accountProvider.GetProductIds(cancellationToken);
    }
}
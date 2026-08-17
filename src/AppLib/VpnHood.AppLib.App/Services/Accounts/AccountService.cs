using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Accounts;

public class AccountService
{
    // How long a cached account may be served before the server is asked again. An expiry that has
    // passed is not the only way an account goes stale: the person can buy on the website, be given
    // a code, or have the backend re-choose one, and NOTHING about that reaches this device — no
    // expiry moves, no local act happens. So the cache ages out on the clock as well, and an
    // account holding no expiry at all (a free one, a code that never runs out) ages out with it
    // rather than living untouched until the app is restarted.
    private static readonly TimeSpan AccountRecheckInterval = TimeSpan.FromMinutes(5);

    private readonly AsyncLock _refreshLock = new();
    private Account? _account;
    private DateTime _lastRefreshAttemptTime = DateTime.MinValue;
    private readonly AppSettingsService _settingsService;
    private readonly IAccountProvider _accountProvider;
    private readonly ClientProfileService _clientProfileService;
    private readonly string _storageFolderPath;
    private readonly string _accountFilePath;

    public AccountService(
        AppSettingsService settingsService,
        IAccountProvider accountProvider,
        ClientProfileService clientProfileService,
        string storageFolderPath)
    {
        _settingsService = settingsService;
        _accountProvider = accountProvider;
        _clientProfileService = clientProfileService;
        _storageFolderPath = storageFolderPath;
        _accountFilePath = Path.Combine(storageFolderPath, "account.json");
        AuthenticationService = new AuthenticationService(this, accountProvider.AuthenticationProvider);
        BillingService = accountProvider.Billing != null
            ? new BillingService(this, accountProvider.Billing)
            : null;
    }


    /// <summary>Does the backend already know a store subscription for this account? This is the
    /// silent-restore skip, not the premium question — a code-served account still wants the store
    /// asked, because its home store's subscription outranks the code (lifecycle §8).</summary>
    public async Task<bool> HasSubscription(bool useCache, CancellationToken cancellationToken)
    {
        var account = await GetAccount(useCache, cancellationToken).Vhc();
        return account?.Subscription != null;
    }

    /// <summary>
    /// Is the account already served (lifecycle §8) — an active store subscription or the account's
    /// chosen access code, either channel? This is the purchase-prevention question, and it must be
    /// asked fresh (useCache false) before the store's payment sheet opens: after the sheet there
    /// is no undo, and on at least one store no refund we can trigger.
    /// </summary>
    public async Task<bool> IsServed(bool useCache, CancellationToken cancellationToken)
    {
        var account = await GetAccount(useCache, cancellationToken).Vhc();
        return account?.Subscription != null || account?.AccessCodeInfo != null;
    }

    public AuthenticationService AuthenticationService { get; }

    public BillingService? BillingService { get; }

    public Task<Account?> GetAccount(CancellationToken cancellationToken)
    {
        return GetAccount(useCache: true, cancellationToken);
    }

    private async Task<Account?> GetAccount(bool useCache, CancellationToken cancellationToken)
    {
        if (AuthenticationService.UserId == null) {
            ClearAccount();
            return null;
        }

        // Get from local cache
        if (useCache) {
            _account ??= JsonUtils.TryDeserializeFile<Account>(_accountFilePath, logger: VhLogger.Instance);
            // BOTH must hold: nothing it carries has expired, AND it is younger than the recheck
            // interval. Either one alone leaves a whole class of change invisible — an expiry that
            // passed, or a change the server made that no expiry announces.
            if (_account != null && IsCacheCurrent(_account) && !IsRecheckDue())
                return _account;
        }

        // Update cache from server and update local cache. If the server is
        // unreachable, a stale account is still better than none for display.
        try {
            await Refresh(cancellationToken);
        }
        catch (Exception ex) when (_account != null) {
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
        return _account;
    }

    // The cached account is current while nothing it holds has expired: the subscription's period
    // end AND the access code's own clock both count — a code-served account goes stale the moment
    // its code runs out, exactly like a subscription-served one.
    private static bool IsCacheCurrent(Account account)
    {
        DateTime?[] expirations = [account.Subscription?.ExpirationTime, account.AccessCodeInfo?.ExpirationTime];
        return expirations.All(x => x == null || x.Value.ToUniversalTime() > DateTime.UtcNow);
    }

    private bool IsRecheckDue()
    {
        return DateTime.UtcNow - _lastRefreshAttemptTime >= AccountRecheckInterval;
    }

    /// <summary>
    /// Account deletion: the backend erases the person everywhere, then this device forgets the account —
    /// account-granted premium included. The refresh below is what strips the account-applied access
    /// code — whichever channel delivered it — because an account-applied
    /// code leaves with its account (lifecycle §8). Only a code the person typed themselves survives;
    /// the farewell mail is the way back for the rest.
    /// <para>
    /// The paid entitlement itself is not destroyed: the store still owns that subscription, the
    /// backend deliberately does not cancel it, and signing in again brings it back by itself.
    /// </para>
    /// </summary>
    public async Task DeleteAccount(IUiContext uiContext, CancellationToken cancellationToken)
    {
        await _accountProvider.DeleteAccount(cancellationToken).Vhc();

        // Signing this device out is part of deleting, not a step the caller may forget: a device
        // still holding a session for an erased account would silently re-create one on the next
        // token renewal. It runs AFTER the backend agreed — a refused deletion must leave the
        // session intact so the person can come back and retry. The revoke it sends is a harmless
        // 204 by then; what matters locally is the session file and the IdP's cached credential.
        await _accountProvider.AuthenticationProvider.SignOut(uiContext, cancellationToken).Vhc();

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
        _account = await _accountProvider.GetAccount(cancellationToken).Vhc();
        Directory.CreateDirectory(_storageFolderPath);
        await File.WriteAllTextAsync(_accountFilePath, JsonSerializer.Serialize(_account), cancellationToken).Vhc();

        // the current profile is what carries premium on this device
        var currentProfile = GetCurrentProfile();
        if (currentProfile is null)
            throw new InvalidOperationException("Could not refresh account when there is no current client profile.");

        ApplyAccountAccessCode(currentProfile);
    }

    /// <summary>
    /// Premium-at-sign-in (lifecycle §8): apply THE one access code the backend chose for this
    /// account. The app never sees a list, never picks and never asks — the choice is the server's,
    /// recomputed on its side at every read. It arrives ranked, and
    /// <see cref="Account.Subscription" /> says which rank: a code backed by an active store
    /// subscription outranks whatever the device already holds, because that is what the person is
    /// paying for right now; a code from any other channel only fills a device that has none. Either
    /// way it is recorded as ACCOUNT-GRANTED, so it leaves with the account (sign-out, deletion).
    /// Nothing is confiscated: the code keeps working for everyone using it, the farewell mail
    /// carries it, and typing it back in makes it the person's own. Only a code the person typed
    /// themselves is theirs to keep.
    /// </summary>
    private void ApplyAccountAccessCode(ClientProfile currentProfile)
    {
        // Nothing left to serve: signed out, deleted, a session the portal no longer honours, or a
        // subscription that ended. The code the account put here is spent, and leaving it on the
        // profile would hold every LOCAL premium gate open — ClientProfile.IsPremium is true whenever
        // an access code is set — long after the server stopped honouring it, which is the app
        // promising premium and then failing to connect.
        var accessCode = _account?.AccessCodeInfo?.AccessCode;
        if (string.IsNullOrEmpty(accessCode)) {
            RemoveAccountAccessCode();
            return;
        }

        // A code the person typed is only ever displaced by a subscription-backed one. Otherwise the
        // account's code fills an empty device — or replaces the code this same account left here on
        // an earlier read, since the backend recomputes its choice every time.
        if (_account?.Subscription is null &&
            currentProfile is { AccessCode: not null, IsAccessCodeFromAccount: false })
            return;

        _clientProfileService.Update(currentProfile.ClientProfileId,
            new ClientProfileUpdateParams {
                AccessCode = new Patch<string?>(accessCode),
                IsAccessCodeFromAccount = true
            });
    }

    /// <summary>Forget the account on this device, premium included.</summary>
    private void ClearAccount()
    {
        if (File.Exists(_accountFilePath))
            File.Delete(_accountFilePath);

        _account = null;
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

    /// <summary>The store product ids this app may sell — see <see cref="IAccountProvider.GetProductIds" />.</summary>
    public Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken)
    {
        return _accountProvider.GetProductIds(cancellationToken);
    }
}
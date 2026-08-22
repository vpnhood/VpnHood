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
    // A floor between attempts, NOT a poll: it only applies once the cached account has actually
    // expired and the server could not be reached. Without it an expired account would ask again on
    // every single read while the portal is down.
    private static readonly TimeSpan RefreshRetryInterval = TimeSpan.FromMinutes(5);

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
    /// Is the account already served (lifecycle §8) — a delivered store subscription or the account's
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

            // Trust the cache while nothing it carries has expired, and ask NOTHING in the meantime.
            // A working credential needs no permission to go on working, and the portal is precisely
            // the thing this app's users often cannot reach — so a device that is fine says nothing
            // and costs nothing. Free accounts carry no expiry at all and therefore never call out,
            // which is the point: they are the many.
            // What the account gained meanwhile — a website purchase, a code from support — arrives
            // when something asks: an explicit refresh, a code typed here, a refusal, or a purchase.
            if (_account != null && IsCacheCurrent(_account))
                return _account;

            // Expired, but the portal may simply be unreachable. One attempt per interval, then the
            // stale account stands: hammering a blocked portal helps nobody.
            if (_account != null && IsRetryThrottled())
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

    private bool IsRetryThrottled()
    {
        return DateTime.UtcNow - _lastRefreshAttemptTime < RefreshRetryInterval;
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

    /// <summary>
    /// Hand the account a code somebody typed on this device while the portal could not be reached.
    /// That is ordinary rather than exotic here — VpnHood is used where the portal itself is blocked,
    /// and connecting is usually what unblocks it, which is why this is called after a successful
    /// connection as well as at every refresh (keyring plan §6).
    /// <para>
    /// Best-effort on the connection path: the person just connected successfully and must not be
    /// shown an error about a background upload. The code stays unsynced and is offered again next
    /// time.
    /// </para>
    /// </summary>
    public async Task TryUploadPendingAccessCode(CancellationToken cancellationToken)
    {
        try {
            await UploadPendingAccessCode(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not upload the pending access code to the account.");
        }
    }

    private async Task UploadPendingAccessCode(CancellationToken cancellationToken)
    {
        // nobody to upload to; a signed-out device's code is its own
        if (_accountProvider.AuthenticationProvider.UserId is null)
            return;

        var currentProfile = GetCurrentProfile();
        if (currentProfile is not { AccessCode: not null, IsAccessCodeSynced: false })
            return;

        await _accountProvider.SetAccessCode(currentProfile.AccessCode, cancellationToken).Vhc();

        // the account has taken it, so the device owes nothing for it any more
        _clientProfileService.SetAccountAccessCode(currentProfile.ClientProfileId, currentProfile.AccessCode);
    }

    /// <summary>
    /// The access server refused a code on this device: tell the account, so the ranking stops
    /// handing the same dead credential to every device the person owns (keyring plan §4).
    /// <para>
    /// Reported only when the refused code IS the one the account is serving. A code typed on this
    /// device and never uploaded is nobody's business but this device's, and the backend checks the
    /// same thing again atomically — a report overtaken by a different code is dropped there rather
    /// than guessed about here.
    /// </para>
    /// Best-effort by design: the refusal has already been recorded on the profile and shown to the
    /// person, and failing to tell the account must never become a second error on top of it.
    /// </summary>
    public async Task TryReportAccessCodeRejected(string refusedAccessCode, CancellationToken cancellationToken)
    {
        if (_account?.AccessCodeInfo?.AccessCode != refusedAccessCode)
            return;

        try {
            await _accountProvider.ReportAccessCodeRejected(refusedAccessCode, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not tell the account that its access code was refused.");
        }
    }

    public async Task Refresh(CancellationToken cancellationToken)
    {
        // Serialized: this writes account.json and rewrites the current profile, and it is now
        // reached from the background at startup as well as from the UI. Two at once collide on the
        // file itself — the second writer finds it still open — and can leave the profile carrying
        // the loser's access code.
        using var refreshLock = await _refreshLock.LockAsync(cancellationToken).Vhc();

        // A code typed here while the portal was unreachable is offered BEFORE the account is read,
        // so the answer applied below already accounts for it. Without this the refresh would
        // silently throw away a decision the person made (keyring plan §6).
        await UploadPendingAccessCode(cancellationToken).Vhc();

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
    /// Apply THE one code the backend ranked for this account (keyring plan §2). There is no
    /// precedence to weigh here and nothing to choose: the backend ranks everything the account
    /// holds — what is being paid for right now first, then the best of the rest — and recomputes
    /// the winner on every read. A signed-in device holds only account state, so whatever arrives
    /// simply replaces what is on the profile.
    /// </summary>
    private void ApplyAccountAccessCode(ClientProfile currentProfile)
    {
        // No account at all — deleted here or on another device, or a session the portal no longer
        // honours. Premium must not outlive the account that granted it, and must not be carried into
        // whatever account signs in next.
        if (_account is null) {
            RemoveAccountAccessCode();
            return;
        }

        // The account is still here and simply ranked nothing: a subscription ran out, its last code
        // did, or the panel stopped offering it. The code STAYS on the profile (keyring plan §8).
        // Clearing it would drop ClientProfile.IsPremium and turn the build into its own free edition
        // on nobody's decision — premium locations gone, promotion banner back, nobody told. It costs
        // nothing to leave: the access server gates every premium-by-code feature again at connect
        // time, so a spent code opens the local toggles and then fails the connection, which is where
        // the person is told and offered Restore Premium or a new code. That is the road a refusal
        // already takes, and there is deliberately only one.
        var accessCode = _account.AccessCodeInfo?.AccessCode;
        if (string.IsNullOrEmpty(accessCode))
            return;

        // It came from the account, so this device owes nothing for it. Handing the same credential
        // back churns nothing and keeps its refusal — the store compares before it writes.
        _clientProfileService.SetAccountAccessCode(currentProfile.ClientProfileId, accessCode);
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
    /// Take the code away WITH THE ACCOUNT: signed out, deleted here, deleted on another device, or a
    /// session the portal no longer honours. A signed-in device holds only account state (keyring
    /// plan §6), so there is nothing to keep back — the code came from the account and leaves with
    /// it. Leaving it behind would keep serving premium off an account that no longer exists, and
    /// would carry paid access into whatever account signs in next.
    /// <para>
    /// The account merely running out of codes is NOT this: nobody chose it, the account is still
    /// here, and the code stays so the app does not quietly demote itself. See
    /// <see cref="ApplyAccountAccessCode" />.
    /// </para>
    /// <para>
    /// Nothing is destroyed: the store still owns that subscription, so Restore Purchase brings it
    /// back onto a new account, and an uploaded code stays in the account it was uploaded to. Only a
    /// code typed on a device that never signed in is the device's own, and this path never runs
    /// there.
    /// </para>
    /// </summary>
    private void RemoveAccountAccessCode()
    {
        var currentProfile = GetCurrentProfile();
        if (currentProfile?.AccessCode == null)
            return;

        // A code the account never took never became the account's, so it is not the account's to
        // take away — it stays with the device (keyring plan §6). This is the one exception to
        // "a signed-in device holds only account state", and the only provenance the design keeps.
        if (!currentProfile.IsAccessCodeSynced)
            return;

        _clientProfileService.Update(currentProfile.ClientProfileId,
            new ClientProfileUpdateParams { AccessCode = new Patch<string?>(null) });
    }

    /// <summary>
    /// The profile that carries premium on this device: the selected one, or — when nothing is
    /// selected yet — the only profile there is. The account world is built for a single-profile app,
    /// and an account build ships a built-in key that <c>VpnHoodApp</c> selects at construction, so
    /// the fallback normally never runs.
    /// <para>
    /// If that assumption ever breaks it throws instead of guessing. Picking one of several at random
    /// would put a paid code on an arbitrary profile and nobody would find out — a silent wrong
    /// answer that costs far more than a loud failure.
    /// </para>
    /// </summary>
    private ClientProfile? GetCurrentProfile()
    {
        var selected = _clientProfileService.FindById(_settingsService.UserSettings.ClientProfileId ?? Guid.Empty);
        if (selected != null)
            return selected;

        var profiles = _clientProfileService.List();
        return profiles.Length switch {
            0 => null,
            1 => profiles[0],
            _ => throw new InvalidOperationException(
                "Could not tell which profile carries premium: none is selected and this app holds more " +
                "than one. The account services assume a single-profile app.")
        };
    }

    /// <summary>The store product ids this app may sell — see <see cref="IAccountProvider.GetProductIds" />.</summary>
    public Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken)
    {
        return _accountProvider.GetProductIds(cancellationToken);
    }
}

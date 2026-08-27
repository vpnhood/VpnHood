using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Services.Accounts;

public class AuthenticationService(
    AccountService accountService,
    IAuthenticationProvider accountProvider)
    : IDisposable
{
    public IReadOnlyList<string> ProviderIds => accountProvider.ProviderIds;
    public string? UserId => accountProvider.UserId;

    /// <summary>
    /// Zero-tap sign-in restoration probe: when nobody is signed in, asking the provider for an
    /// access token is its cue to try re-establishing a session silently (a restored device may
    /// hold a credential that signs back in with no interaction). Cheap after the first call - the
    /// provider attempts it once per process. True when a session exists afterwards.
    /// </summary>
    public async Task<bool> TryRestoreSession(CancellationToken cancellationToken)
    {
        if (UserId != null)
            return true;
        var accessToken = await accountProvider.GetAccessToken(cancellationToken).Vhc();
        return accessToken != null;
    }

    public async Task<SignInResult> SignIn(IUiContext uiContext, SignInOptions signInOptions,
        CancellationToken cancellationToken)
    {
        var result = await accountProvider.SignIn(uiContext, signInOptions, cancellationToken).Vhc();

        // anything but SignedIn means NOTHING is signed in yet — the caller repeats SignIn with the
        // second-factor code; refreshing or querying the store now would be work for nobody
        if (result.State != SignInState.SignedIn)
            return result;

        await accountService.Refresh(cancellationToken).Vhc();

        // The quiet half of "coming back" (lifecycle §7): right after the session is established,
        // ask the store what this store account owns and present anything the backend does not
        // already know. Fire-and-forget by design — sign-in never waits for it, its failure never
        // fails the sign-in, presenting a known purchase is an idempotent no-op backend-side, and
        // the visible Restore control stays as the retry. It must be the SILENT kind of store
        // query (both providers read what the device already knows; nothing may prompt for store
        // credentials on every sign-in), and it is skipped only when a store subscription is
        // already known — a code-served account still asks, because the home store's subscription
        // outranks the code (lifecycle §8).
        _ = TrySilentRestore(uiContext);
        return result;
    }

    private async Task TrySilentRestore(IUiContext uiContext)
    {
        try {
            var billingService = accountService.BillingService;
            if (billingService is null ||
                await accountService.HasSubscription(useCache: true, CancellationToken.None).Vhc())
                return;

            await billingService.RestorePurchase(uiContext, CancellationToken.None).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "The silent post-sign-in store query restored nothing.");
        }
    }

    public async Task SignOut(IUiContext uiContext, CancellationToken cancellationToken)
    {
        await accountProvider.SignOut(uiContext, cancellationToken).Vhc();

        // the refresh finds no account and takes the account-sourced access code with it
        await accountService.Refresh(cancellationToken).Vhc();
    }

    public void Dispose()
    {
        accountProvider.Dispose();
    }
}

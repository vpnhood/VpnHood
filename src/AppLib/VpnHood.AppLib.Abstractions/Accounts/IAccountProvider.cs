using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions.Accounts;

public interface IAccountProvider
{
    IAuthenticationProvider AuthenticationProvider { get; }

    /// <summary>Null when the app has no in-app purchasing.</summary>
    AppBilling? Billing { get; }

    /// <summary>
    /// The signed-in account, or null. <see cref="Account.AccessCodeInfo" /> carries THE one access
    /// code serving this account, whichever channel delivered it — the backend chooses it and
    /// recomputes the choice on every read, so the app never sees a list and never has to pick
    /// (lifecycle §8). <see cref="Account.Subscription" /> says whether that code is backed by an
    /// active store subscription, which is what decides how forcefully it is applied.
    /// </summary>
    Task<Account?> GetAccount(CancellationToken cancellationToken);

    /// <summary>
    /// The store product ids this app may sell — the backend's answer, because no store can list an
    /// app's own catalog: StoreKit and Play Billing only price ids they are given, so the ids must
    /// come from the side that redeems them. <see cref="AppBilling.Provider" /> prices exactly this
    /// list. Empty when nothing is sellable.
    /// </summary>
    Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken);

    /// <summary>
    /// Delete the signed-in account everywhere: the backend erases the person, every device is
    /// signed out, and a later sign-in creates a brand-new account. Nothing blocks it — website
    /// billing is cancelled at the end of its paid period backend-side, and a store subscription is
    /// deliberately left untouched (signing in again brings it back by itself; only the person can
    /// cancel it, in their store). It never ends already-paid access. Mandatory, not optional: any
    /// provider with sign-in owes an in-app deletion path by store policy (Apple 5.1.1(v), Google
    /// Play).
    /// <para>
    /// The backend call only — no ui context, because there is nothing to show. Signing THIS device
    /// out belongs to the app, which does it after this returns, so a refusal leaves the session
    /// intact and the person can retry.
    /// </para>
    /// </summary>
    Task DeleteAccount(CancellationToken cancellationToken);
}

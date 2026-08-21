using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions.Accounts;

public interface IAccountProvider
{
    IAuthenticationProvider AuthenticationProvider { get; }

    /// <summary>Null when the app has no in-app purchasing.</summary>
    AppBilling? Billing { get; }

    /// <summary>
    /// The signed-in account, or null. <see cref="Account.AccessCodeInfo" /> carries THE one code
    /// serving this account, whichever channel delivered it: the backend ranks everything the
    /// account holds and recomputes the winner on every read, so the app never sees a list and never
    /// has to pick (keyring plan §2). <see cref="Account.Subscription" /> says whether a store
    /// subscription is what is paying right now. A displayed expiry is advisory; only an
    /// access-server refusal ends use of the credential.
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
    /// Upload a code the person typed into the account's ONE upload slot, or empty the slot with
    /// null. An uploaded code then takes part in the account's ranking exactly like a code the
    /// account bought — there are no imported-only rules anywhere (keyring plan §5).
    /// <para>
    /// The backend takes the code ON TRUST: it stores any string of valid access-code shape without
    /// proving that the code exists, because only the access server can say whether a code works and
    /// it says so at use time. There is therefore no <i>not found</i> answer to inspect — the call
    /// either succeeds or fails on a network error.
    /// </para>
    /// <para>
    /// Uploading transfers the code to the account, and the account does not hand it back when it is
    /// deleted. A different code replaces the slot atomically; uploading a code the account already
    /// holds does not consume the slot and turns that code's <i>auto selectable</i> flag back on,
    /// because typing a code is saying <i>use this</i>.
    /// </para>
    /// <para>
    /// Nothing here is ordered by time. Two devices that both upload are resolved by whichever call
    /// reaches the backend last, so a code typed during an outage can win over a later decision made
    /// elsewhere — accepted deliberately, because ordering it would cost a whole sync protocol to
    /// settle an argument almost nobody is having.
    /// </para>
    /// A provider with no account-side code storage throws <see cref="NotSupportedException" />.
    /// </summary>
    /// <param name="accessCode">The code to upload, or null to idempotently empty the slot.</param>
    Task SetAccessCode(string? accessCode, CancellationToken cancellationToken);

    /// <summary>
    /// Tell the account what a connection attempt revealed about a code's clock. This is the ONLY
    /// way the backend ever learns an expiry: it cannot look one up for a code it never sold, and
    /// choosing not to discover expiries is what keeps uploaded codes free of special rules
    /// (keyring plan §4).
    /// <para>
    /// Called after a successful connection with the expiry the access server gave, and after an
    /// authoritative refusal with the moment of the refusal — a code that was just refused has
    /// expired as of now, whatever the portal believes. The backend records it against THIS account
    /// only, and it never moves the upload slot's own stamp.
    /// </para>
    /// A provider with no account-side code storage throws <see cref="NotSupportedException" />.
    /// </summary>
    Task ReportAccessCodeExpiration(string accessCode, DateTime expirationTime,
        CancellationToken cancellationToken);

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

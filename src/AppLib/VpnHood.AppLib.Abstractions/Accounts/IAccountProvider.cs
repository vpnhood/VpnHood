using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Abstractions.Accounts;

namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// Provides the app's account backend: the sign-in, the signed-in person's account and its premium code, the products
/// the app may sell, and the account's deletion.
/// </summary>
public interface IAccountProvider
{
    /// <summary>Gets the sign-in: who is signed in, and the credential that proves it.</summary>
    IAuthenticationProvider AuthenticationProvider { get; }

    /// <summary>Null when the app has no in-app purchasing.</summary>
    AppBilling? Billing { get; }

    /// <summary>
    /// The signed-in account, or null. <see cref="Account.AccessCodeInfo" /> carries THE one code
    /// serving this account, whichever channel delivered it: the backend ranks everything the
    /// account holds and recomputes the winner on every read, so the app never sees a list and never
    /// has to pick. <see cref="Account.Subscription" /> says whether a store
    /// subscription is what is paying right now. A displayed expiry is advisory; only an
    /// access-server refusal ends use of the credential.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the signed-in account, or <see langword="null"/> where nobody is signed in.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">The account backend refused the request.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<Account?> GetAccount(CancellationToken cancellationToken);

    /// <summary>
    /// The store product ids this app may sell — the backend's answer, because no store can list an
    /// app's own catalog: StoreKit and Play Billing only price ids they are given, so the ids must
    /// come from the side that redeems them. <see cref="AppBilling.Provider" /> prices exactly this
    /// list. Empty when nothing is sellable.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the product IDs.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">The account backend refused the request.</exception>
    /// <exception cref="InvalidOperationException">The app has no store, so it sells nothing.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken);

    /// <summary>
    /// Upload a code the person typed into the account's ONE upload slot, or empty the slot with
    /// null. The uploaded code ranks above every code of the account that nobody is paying for,
    /// because typing a code is saying <i>use this</i>, and below anything being paid for right now.
    /// <para>
    /// The backend takes the code ON TRUST: it stores any string of valid access-code shape without
    /// proving that the code exists, because only the access server can say whether a code works and
    /// it says so at use time. There is therefore no <i>not found</i> answer to inspect.
    /// </para>
    /// <para>
    /// Uploading transfers the code to the account, and the account does not hand it back when it is
    /// deleted. Every upload replaces the slot's code, a code the account already holds too, and
    /// clears a refusal reported for that code (<see cref="ReportAccessCodeRejected" />).
    /// </para>
    /// <para>
    /// Nothing here is ordered by time. Two devices that both upload are resolved by whichever call
    /// reaches the backend last, so a code typed during an outage can win over a later decision made
    /// elsewhere — accepted deliberately, because ordering it would cost a whole sync protocol to
    /// settle an argument almost nobody is having.
    /// </para>
    /// </summary>
    /// <param name="accessCode">The code to upload, or null to idempotently empty the slot.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// The account backend refused the request, as the portal refuses a string that is not shaped as a code.
    /// </exception>
    /// <exception cref="NotSupportedException">The provider keeps no codes on the account.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetAccessCode(string? accessCode, CancellationToken cancellationToken);

    /// <summary>
    /// Tell the account that the access server REFUSED its current code — the only thing a device
    /// ever reports, and the whole of what the backend's ranking needs. Every code an account holds
    /// is either eligible or rejected: no expiry dates, no reasons, no observation times, and no
    /// successful-connection counterpart to this call.
    /// <para>
    /// The backend applies it only while this is still the account's CURRENT code, so a delayed
    /// refusal that has been overtaken by a different one changes nothing. It is recorded against
    /// THIS account alone — the same bearer code may be serving somebody else perfectly well — and
    /// against every entry holding that string, because identical codes are the same credential.
    /// </para>
    /// <para>
    /// Rejection skips a code, it never deletes one, and it never skips a code being paid for right
    /// now: that one is still served, and the refusal is only recorded. Where every code the account
    /// holds has been refused, the account keeps serving the one refused last. Uploading the code
    /// again through <see cref="SetAccessCode" /> clears its refusal, which is all that <i>Retry</i>
    /// needs to be — and the reason the one edge case here is survivable: remove-then-re-add of the
    /// same string can be caught by a late report, and one more retry undoes it.
    /// </para>
    /// The code travels in the authenticated request body and never in a URL.
    /// </summary>
    /// <param name="accessCode">The code the access server refused.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">The account backend refused the request.</exception>
    /// <exception cref="NotSupportedException">The provider keeps no codes on the account.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task ReportAccessCodeRejected(string accessCode, CancellationToken cancellationToken);

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
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the backend has deleted the account.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">The account backend refused the request.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task DeleteAccount(CancellationToken cancellationToken);
}

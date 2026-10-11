using VpnHood.AppLib.Api.Accounts;

namespace VpnHood.AppLib.Api;

public interface IAccountApi
{
    /// <summary>
    /// Establish the account session. A result whose State is not SignedIn means nothing is signed
    /// in yet — repeat the call with TwoFactorCode. NewBackupCode, when present, must be shown once.
    /// </summary>
    Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken);
    Task SignOut(CancellationToken cancellationToken);

    /// <summary>
    /// Delete the account everywhere. Never touches a store subscription — signing in again brings
    /// it back by itself; only the person can cancel it, in their store.
    /// </summary>
    Task Delete(CancellationToken cancellationToken);

    Task Refresh(CancellationToken cancellationToken);
    Task<Account?> Get(CancellationToken cancellationToken);

    /// <summary>
    /// The account website's page for a forgotten password, or null when there is none — no
    /// account provider, or one without such a page. Asked when the password form opens: the form
    /// offers "forgot password" only when a page comes back.
    /// </summary>
    Task<Uri?> GetPasswordResetUrl(CancellationToken cancellationToken);
}

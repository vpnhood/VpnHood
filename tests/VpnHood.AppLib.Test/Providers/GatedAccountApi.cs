using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.Accounts;

namespace VpnHood.AppLib.Test.Providers;

// The app's account API as a slow or broken network shows it to a UI: a read can answer late, with
// what the app held when it was asked, or fail.
internal class GatedAccountApi(IAccountApi inner) : IAccountApi
{
    // the next read answers only when this completes; HeldReadDone says it has read
    public Task? HoldNextGet { get; set; }
    public TaskCompletionSource HeldReadDone { get; } = new();
    public int FailingGets { get; set; }
    public int GetCalls { get; private set; }

    public async Task<Account?> Get(CancellationToken cancellationToken)
    {
        GetCalls++;
        if (FailingGets > 0) {
            FailingGets--;
            throw new HttpRequestException("The test's network is down.");
        }

        var account = await inner.Get(cancellationToken);
        var hold = HoldNextGet;
        HoldNextGet = null;
        if (hold != null) {
            HeldReadDone.TrySetResult();
            await hold;
        }

        return account;
    }

    public Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken) =>
        inner.SignIn(signInOptions, cancellationToken);

    public Task SignOut(CancellationToken cancellationToken) => inner.SignOut(cancellationToken);
    public Task Delete(CancellationToken cancellationToken) => inner.Delete(cancellationToken);
    public Task Refresh(CancellationToken cancellationToken) => inner.Refresh(cancellationToken);

    public Task<Uri?> GetPasswordResetUrl(CancellationToken cancellationToken) =>
        inner.GetPasswordResetUrl(cancellationToken);
}

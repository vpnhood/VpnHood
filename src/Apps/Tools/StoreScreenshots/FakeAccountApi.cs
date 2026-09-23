using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.Accounts;

namespace VpnHood.App.StoreScreenshots;

// No account: the fixture's builds do not support one, and a page that asks anyway is told there
// is none, which is what a signed-out app says.
internal sealed class FakeAccountApi : IAccountApi
{
    public Task<Account?> Get(CancellationToken cancellationToken) => Task.FromResult<Account?>(null);
    public Task Refresh(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken) => throw UnmockedCalls.Record("Account.SignIn");
    public Task SignOut(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Account.SignOut");
    public Task Delete(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Account.Delete");
}

using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.UiContexts;
using VpnHood.AppLib.Test.Providers;

namespace VpnHood.AppLib.Test.Tests;

// Each UI reads the account again when the state's AccountTime moves, so a sign-in from a paired
// browser reaches the TV's own screen. The time must move on a sign-in and a sign-out, and stay put
// on a plain read, or a UI would read the account at every state.
[TestClass]
public class AccountTimeTest : TestAppBase
{
    [TestMethod]
    public async Task Moves_on_sign_in_and_sign_out_only()
    {
        var accountProvider = new TestAccountProvider {
            Account = new Account {
                UserId = Guid.Empty.ToString(),
                Email = "buyer@example.com",
                Subscription = null,
                AccessCodeInfo = null
            }
        };
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AccountProvider = accountProvider;
        appOptions.AccessKeys = [TestAppHelper.CreateOfflineToken().ToAccessKey()];
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var accountService = app.Services.AccountService
                             ?? throw new InvalidOperationException("The test app has no account service.");

        var signedOutTime = app.State.AccountTime;
        Assert.IsNotNull(signedOutTime);
        for (var i = 0; i < 3; i++)
            Assert.IsNull(await accountService.GetAccount(TestCt));
        Assert.AreEqual(signedOutTime, app.State.AccountTime, "a signed-out read changes nothing");

        await accountService.AuthenticationService.SignIn(AppUiContext.RequiredContext,
            new SignInOptions { ProviderId = AuthProviders.Google }, TestCt);
        var signedInTime = app.State.AccountTime;
        Assert.IsTrue(signedInTime > signedOutTime, "a sign-in moves the time on");

        Assert.IsNotNull(await accountService.GetAccount(TestCt));
        Assert.AreEqual(signedInTime, app.State.AccountTime, "a cached read changes nothing");

        await accountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, TestCt);
        var signedOutAgainTime = app.State.AccountTime;
        Assert.IsTrue(signedOutAgainTime > signedInTime, "a sign-out moves the time on");

        Assert.IsNull(await accountService.GetAccount(TestCt));
        Assert.AreEqual(signedOutAgainTime, app.State.AccountTime, "a signed-out read changes nothing");
    }

    [TestMethod]
    public async Task Null_in_a_build_with_no_accounts()
    {
        await using var app = TestAppHelper.CreateClientApp();
        Assert.IsNull(app.State.AccountTime);
    }
}

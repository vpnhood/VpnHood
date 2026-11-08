using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.UiContexts;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Accounts;
using VpnHood.AppLib.Test.Providers;
using VpnHood.AppUi.Common;

namespace VpnHood.AppLib.Test.Tests;

// A UI's account as it follows the app's (VhApp): a sign-in or sign-out made from another UI - the
// TV's own screen and a paired browser - reaches this one with the next state, an older read answering
// late does not put back what it replaced, and a read that fails is tried again without holding up the
// state.
[TestClass]
[DoNotParallelize] // VhApp is the process's one UI
public class VhAppAccountTest : TestAppBase
{
    private const string Email = "buyer@example.com";

    private VpnHoodApp CreateApp()
    {
        var accountProvider = new TestAccountProvider {
            Account = new Account {
                UserId = Guid.Empty.ToString(),
                Email = Email,
                Subscription = null,
                AccessCodeInfo = null
            }
        };
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AccountProvider = accountProvider;
        appOptions.AccessKeys = [TestAppHelper.CreateOfflineToken().ToAccessKey()];
        return TestAppHelper.CreateClientApp(appOptions);
    }

    // this UI started on the app, signed out, through an account API it can hold up or break
    private async Task<GatedAccountApi> StartUi(VpnHoodApp app)
    {
        var api = app.Api;
        var accountApi = new GatedAccountApi(api.Account);
        await VhApp.Init(new VpnHoodApi(api.App, api.VpnProfiles, accountApi, api.Billing, api.Intents,
            api.ProxyEndPoints), TestCt);
        await VhApp.LoadAccount(false, TestCt);
        Assert.IsNull(VhApp.Account);
        return accountApi;
    }

    private static AccountService GetAccountService(VpnHoodApp app)
    {
        return app.Services.AccountService
               ?? throw new InvalidOperationException("The test app has no account service.");
    }

    // the other UI's sign-in and sign-out
    private Task SignIn(VpnHoodApp app)
    {
        return GetAccountService(app).AuthenticationService.SignIn(AppUiContext.RequiredContext,
            new SignInOptions { ProviderId = AuthProviders.Google }, TestCt);
    }

    private Task SignOut(VpnHoodApp app)
    {
        return GetAccountService(app).AuthenticationService.SignOut(AppUiContext.RequiredContext, TestCt);
    }

    [TestMethod]
    public async Task Another_UIs_sign_in_and_sign_out_reach_this_one()
    {
        await using var app = CreateApp();
        var accountApi = await StartUi(app);

        await SignIn(app);
        await VhApp.ReloadState(TestCt);
        Assert.AreEqual(Email, VhApp.Account?.Email, "the state's next read brings the sign-in");

        var getCalls = accountApi.GetCalls;
        for (var i = 0; i < 3; i++)
            await VhApp.ReloadState(TestCt);
        Assert.AreEqual(getCalls, accountApi.GetCalls, "a state with no change reads no account");

        await SignOut(app);
        await VhApp.ReloadState(TestCt);
        Assert.IsNull(VhApp.Account, "the state's next read brings the sign-out");
    }

    [TestMethod]
    public async Task An_older_read_answering_late_is_not_shown()
    {
        await using var app = CreateApp();
        var accountApi = await StartUi(app);
        await SignIn(app);
        await VhApp.ReloadState(TestCt);
        Assert.IsNotNull(VhApp.Account);

        // a read that has the signed-in account and answers only after the sign-out was shown
        var hold = new TaskCompletionSource();
        accountApi.HoldNextGet = hold.Task;
        var lateRead = VhApp.LoadAccount(false, TestCt);
        await accountApi.HeldReadDone.Task.WaitAsync(TestCt);

        await SignOut(app);
        await VhApp.ReloadState(TestCt);
        Assert.IsNull(VhApp.Account);

        hold.SetResult();
        await lateRead;
        Assert.IsNull(VhApp.Account, "the late answer must not put back the account signed out since");
    }

    [TestMethod]
    public async Task A_failed_read_is_tried_again_and_the_state_still_shows()
    {
        await using var app = CreateApp();
        var accountApi = await StartUi(app);
        var retryDelay = VhApp.AccountRetryDelay;
        VhApp.AccountRetryDelay = TimeSpan.FromMilliseconds(300);
        try {
            await SignIn(app);
            accountApi.FailingGets = 1;
            await VhApp.ReloadState(TestCt);
            Assert.IsNull(VhApp.Account);
            Assert.AreEqual(app.State.AccountTime, VhApp.State.AccountTime,
                "the state is read and kept though its account read failed");

            var getCalls = accountApi.GetCalls;
            await VhApp.ReloadState(TestCt);
            Assert.AreEqual(getCalls, accountApi.GetCalls, "a failed read is not tried again at once");

            await Task.Delay(VhApp.AccountRetryDelay * 2, TestCt);
            await VhApp.ReloadState(TestCt);
            Assert.AreEqual(Email, VhApp.Account?.Email, "the read is tried again after the pause");
        }
        finally {
            VhApp.AccountRetryDelay = retryDelay;
        }
    }
}

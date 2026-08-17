using System.Net;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.Test.Tests;

/// <summary>
/// Premium-at-sign-in (lifecycle §8): the backend hands the app ONE access code or nothing — the app
/// never sees a list, never picks and never asks. The code is applied as ACCOUNT-GRANTED, so it
/// leaves the device with the account (sign-out, deletion); only a code the person typed themselves
/// is theirs to keep. A code backed by a store subscription outranks even a typed one, and the app
/// owns no remove act for account codes at all.
/// </summary>
[TestClass]
public class AccountAccessCodeTest : TestAppBase
{
    private static Token CreateToken()
    {
        var randomId = Guid.NewGuid();
        return new Token {
            Name = "Default Test Server",
            IssuedAt = DateTime.UtcNow,
            SupportId = "1",
            TokenId = randomId.ToString(),
            Secret = randomId.ToByteArray(),
            ServerToken = new ServerToken {
                HostEndPoints = [IPEndPoint.Parse("127.0.0.1:443")],
                CertificateHash = randomId.ToByteArray(),
                HostName = randomId.ToString(),
                HostPort = 443,
                Secret = randomId.ToByteArray(),
                CreatedTime = DateTime.UtcNow,
                IsValidHostName = false
            }
        };
    }

    private VpnHoodApp CreateAppWithAccount(TestAccountProvider accountProvider)
    {
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AccountProvider = accountProvider;
        appOptions.AccessKeys = [CreateToken().ToAccessKey()];
        return TestAppHelper.CreateClientApp(appOptions);
    }

    private static AccountService GetAccountService(VpnHoodApp app)
    {
        return app.Services.AccountService
               ?? throw new InvalidOperationException("AccountService is not available in the test app.");
    }

    /// <summary>A signed-in person with NO store subscription — served by the account's own code alone.</summary>
    private static Account CreateFreeAccount(string? accessCode = null)
    {
        return new Account {
            UserId = Guid.Empty.ToString(),
            Email = "buyer@example.com",
            Subscription = null,
            AccessCodeInfo = accessCode != null ? new AccessCodeInfo { AccessCode = accessCode } : null
        };
    }

    private static Task SignIn(AccountService accountService)
    {
        return accountService.AuthenticationService.SignIn(AppUiContext.RequiredContext,
            new SignInOptions { ProviderId = AuthProviders.Google }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Sign_in_applies_the_accounts_code_as_account_granted()
    {
        var accountCode = TestAppHelper.BuildAccessCode();
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(accountCode) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);

        await SignIn(accountService);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNotNull(profile.AccessCode,
            "the one code the backend chose must be applied at sign-in — the app itself never picks");
        // the profile info masks codes; the unmasked tail is enough to prove WHICH code was applied
        StringAssert.EndsWith(profile.AccessCode, accountCode[^4..]);
        Assert.IsTrue(profile.IsAccessCodeFromAccount,
            "the account applied it, so it must leave with the account — lifecycle §8; only a typed code is the person's own");
        Assert.IsTrue(profile.IsPremium);
    }

    [TestMethod]
    public async Task Sign_in_with_no_code_applies_nothing()
    {
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount() };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);

        await SignIn(accountService);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNull(profile.AccessCode,
            "the backend answered 'nothing' — signed in, not premium, and the app must not invent a code");
    }

    [TestMethod]
    public async Task A_typed_code_is_never_overwritten_by_the_accounts_code()
    {
        var typedCode = TestAppHelper.BuildAccessCode();
        // the masked profile info exposes only the last 4 digits, so make the tails distinct
        var accountCode = TestAppHelper.BuildAccessCode();
        while (accountCode[^4..] == typedCode[^4..])
            accountCode = TestAppHelper.BuildAccessCode();
        var accountProvider = new TestAccountProvider {
            Account = CreateFreeAccount(accountCode)
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);

        // the person typed a code before signing in — that code is theirs and it works
        var profileId = app.ClientProfileService.List().First().ClientProfileId;
        app.ClientProfileService.Update(profileId, new VpnHood.AppLib.ClientProfiles.ClientProfileUpdateParams {
            AccessCode = new VpnHood.Core.Toolkit.Utils.Patch<string?>(typedCode),
            IsAccessCodeFromAccount = false
        });

        await SignIn(accountService);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNotNull(profile.AccessCode);
        StringAssert.EndsWith(profile.AccessCode, typedCode[^4..],
            "never overwrite a code that is present — replacing a working code is the one destructive move here");
    }

    [TestMethod]
    public async Task A_subscription_code_outranks_a_code_the_user_typed()
    {
        var typedCode = TestAppHelper.BuildAccessCode();
        // the masked profile info exposes only the last 4 digits, so make the tails distinct
        var subscriptionCode = TestAppHelper.BuildAccessCode();
        while (subscriptionCode[^4..] == typedCode[^4..])
            subscriptionCode = TestAppHelper.BuildAccessCode();

        // the backend ranks the two channels itself: this account is served by a store subscription,
        // so the code it hands the app IS the subscription's
        var accountProvider = new TestAccountProvider {
            Account = new Account {
                UserId = Guid.Empty.ToString(),
                Email = "buyer@example.com",
                AccessCodeInfo = new AccessCodeInfo { AccessCode = subscriptionCode },
                Subscription = new Subscription {
                    StoreId = "googleplay",
                    ExpirationTime = DateTime.UtcNow.AddDays(30)
                }
            }
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);

        // the person typed a code of their own before signing in
        var profileId = app.ClientProfileService.List().First().ClientProfileId;
        app.ClientProfileService.Update(profileId, new VpnHood.AppLib.ClientProfiles.ClientProfileUpdateParams {
            AccessCode = new VpnHood.Core.Toolkit.Utils.Patch<string?>(typedCode),
            IsAccessCodeFromAccount = false
        });

        await SignIn(accountService);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNotNull(profile.AccessCode);
        StringAssert.EndsWith(profile.AccessCode, subscriptionCode[^4..],
            "they are paying for the subscription right now — its code is the one that must connect");
        Assert.IsTrue(profile.IsAccessCodeFromAccount,
            "the subscription is the account's own service, so its code leaves with the account");
    }

    [TestMethod]
    public async Task Deletion_takes_the_applied_code_off_the_device()
    {
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(TestAppHelper.BuildAccessCode()) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);
        Assert.IsNotNull(app.CurrentClientProfileInfo?.AccessCode);

        await accountService.DeleteAccount(AppUiContext.RequiredContext, CancellationToken.None);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNull(profile.AccessCode,
            "an account-applied code leaves with the account (lifecycle §8) — the farewell mail is the way back, not the device");
        Assert.AreEqual(1, accountProvider.DeleteAccountCalls);
    }

    [TestMethod]
    public async Task Deletion_signs_this_device_out()
    {
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(TestAppHelper.BuildAccessCode()) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        await accountService.DeleteAccount(AppUiContext.RequiredContext, CancellationToken.None);

        Assert.IsNull(accountProvider.TestAuthenticationProvider.UserId,
            "a device still holding a session for an erased account would silently re-create one");
    }

    [TestMethod]
    public async Task Deletion_refused_by_the_backend_keeps_the_session()
    {
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(TestAppHelper.BuildAccessCode()) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        // the portal's "deletion_blocked": the person must keep the session to come back and retry
        accountProvider.DeleteAccountException = new InvalidOperationException("deletion_blocked");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            accountService.DeleteAccount(AppUiContext.RequiredContext, CancellationToken.None));

        Assert.IsNotNull(accountProvider.TestAuthenticationProvider.UserId,
            "sign-out runs only after the backend agreed");
        Assert.IsNotNull(app.CurrentClientProfileInfo?.AccessCode, "and premium is untouched");
    }

    [TestMethod]
    public async Task Sign_out_takes_the_applied_code_off_the_device()
    {
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(TestAppHelper.BuildAccessCode()) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);
        Assert.IsNotNull(app.CurrentClientProfileInfo?.AccessCode);

        await accountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, CancellationToken.None);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNull(profile.AccessCode,
            "signing out must take the account's code with it — leaving it would carry premium into whatever account signs in next");
    }
}

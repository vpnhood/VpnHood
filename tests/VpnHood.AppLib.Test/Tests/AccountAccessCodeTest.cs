using System.Net;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

/// <summary>
/// The backend hands the app ONE access code or nothing (keyring plan §2). It ranks everything the
/// account holds and recomputes the winner on every read, so the app never sees a list, never picks
/// and never weighs precedence of its own. A signed-in device therefore holds only account state:
/// whatever the account sends replaces what was on the profile, and it leaves again with the account
/// on sign-out or deletion (§6). Uploading a code is the one write the app has, and null empties the
/// account's single slot.
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

    /// <summary>A second code whose last four digits differ, since the profile info masks the rest.</summary>
    private string BuildDistinctAccessCode(string other)
    {
        var code = TestAppHelper.BuildAccessCode();
        while (code[^4..] == other[^4..])
            code = TestAppHelper.BuildAccessCode();
        return code;
    }

    private static void TypeAccessCode(VpnHoodApp app, Guid clientProfileId, string? accessCode)
    {
        app.ClientProfileService.Update(clientProfileId,
            new ClientProfileUpdateParams { AccessCode = new Patch<string?>(accessCode) });
    }

    private static Task SignIn(AccountService accountService)
    {
        return accountService.AuthenticationService.SignIn(AppUiContext.RequiredContext,
            new SignInOptions { ProviderId = AuthProviders.Google }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Sign_in_applies_the_code_the_backend_ranked()
    {
        var accountCode = TestAppHelper.BuildAccessCode();
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(accountCode) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);

        await SignIn(accountService);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNotNull(profile.AccessCode,
            "the one code the backend ranked must be applied at sign-in — the app itself never picks");
        // the profile info masks codes; the unmasked tail is enough to prove WHICH code was applied
        StringAssert.EndsWith(profile.AccessCode, accountCode[^4..]);
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
    public async Task A_code_left_on_the_device_at_sign_in_is_uploaded_not_discarded()
    {
        var typedCode = TestAppHelper.BuildAccessCode();
        var accountCode = BuildDistinctAccessCode(typedCode);
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(accountCode) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);

        // A code typed on this device before it ever signed in. Reaching a signed-in state with a
        // code still on the device means the person chose "sign in and sync my code" at the prompt
        // (§6) — choosing "without it" would have removed it first — so the code is theirs to upload,
        // never something the refresh may quietly overwrite.
        var profileId = app.ClientProfileService.List().First().ClientProfileId;
        TypeAccessCode(app, profileId, typedCode);

        await SignIn(accountService);

        Assert.AreEqual(typedCode, accountProvider.UploadedAccessCode);
        var profile = app.ClientProfileService.Get(profileId);
        Assert.AreEqual(typedCode, profile.AccessCode);
        Assert.IsTrue(profile.IsAccessCodeSynced);
    }

    [TestMethod]
    public async Task A_subscription_code_outranks_the_uploaded_one()
    {
        var uploadedCode = TestAppHelper.BuildAccessCode();
        var subscriptionCode = BuildDistinctAccessCode(uploadedCode);

        // the backend ranks the channels itself: what is being paid for right now comes first, so the
        // code it hands the app IS the subscription's and the uploaded one simply waits behind it
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

        await SignIn(accountService);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNotNull(profile.AccessCode);
        StringAssert.EndsWith(profile.AccessCode, subscriptionCode[^4..],
            "they are paying for the subscription right now — its code is the one that must connect");
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
            "the code came from the account and leaves with it — the farewell mail is the way back, not the device");
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
        Assert.IsNull(profile?.AccessCode,
            "signing out must take the account's code with it — leaving it would carry premium into whatever account signs in next");
    }

    [TestMethod]
    public async Task A_code_typed_into_the_profile_reaches_the_account_slot()
    {
        var oldCode = TestAppHelper.BuildAccessCode();
        var newCode = BuildDistinctAccessCode(oldCode);

        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(oldCode) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        // The profile is the only door: typing a code makes it work HERE first, and the account hears
        // about it afterwards (keyring plan §6, §7). There is no set-code call on the account API.
        var profileId = app.ClientProfileService.List().First().ClientProfileId;
        TypeAccessCode(app, profileId, newCode);
        Assert.IsFalse(app.ClientProfileService.Get(profileId).IsAccessCodeSynced,
            "it works on this device before anyone has told the account");

        await accountService.Refresh(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { newCode }, accountProvider.SetAccessCodeCalls);
        Assert.AreEqual(newCode, accountProvider.UploadedAccessCode);
        StringAssert.EndsWith(app.CurrentClientProfileInfo!.AccessCode, newCode[^4..]);
        Assert.IsTrue(app.ClientProfileService.Get(profileId).IsAccessCodeSynced);
    }

    [TestMethod]
    public async Task A_code_typed_while_the_portal_is_blocked_is_uploaded_at_the_next_refresh()
    {
        var accountCode = TestAppHelper.BuildAccessCode();
        var typedCode = BuildDistinctAccessCode(accountCode);
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(accountCode) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        // the portal is blocked — which is ordinary where VpnHood is used — so the code is typed here
        // and the account never hears about it
        var profileId = app.ClientProfileService.List().First().ClientProfileId;
        accountProvider.SetAccessCodeException = new HttpRequestException("portal unreachable");
        TypeAccessCode(app, profileId, typedCode);
        Assert.IsFalse(app.ClientProfileService.Get(profileId).IsAccessCodeSynced);

        // the connection came up, so the portal is reachable again
        accountProvider.SetAccessCodeException = null;
        await accountService.Refresh(CancellationToken.None);

        var profile = app.ClientProfileService.Get(profileId);
        Assert.AreEqual(typedCode, accountProvider.UploadedAccessCode,
            "the refresh must offer the pending code BEFORE reading the account, or it silently " +
            "overwrites a decision the person made");
        Assert.AreEqual(typedCode, profile.AccessCode);
        Assert.IsTrue(profile.IsAccessCodeSynced);
    }

    [TestMethod]
    public async Task A_pending_code_survives_sign_out_because_the_account_never_took_it()
    {
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount() };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        var profileId = app.ClientProfileService.List().First().ClientProfileId;
        accountProvider.SetAccessCodeException = new HttpRequestException("portal unreachable");
        TypeAccessCode(app, profileId, TestAppHelper.BuildAccessCode());

        await accountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, CancellationToken.None);

        Assert.IsNotNull(app.ClientProfileService.Get(profileId).AccessCode,
            "a code the account never took never became the account's, so it is not the account's to take away");
    }

    [TestMethod]
    public async Task A_working_account_asks_the_portal_nothing()
    {
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(TestAppHelper.BuildAccessCode()) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        // Nothing it holds has expired — no subscription end, no code expiry — so there is nothing to
        // ask about. A credential that works needs no permission to go on working, and the portal is
        // exactly what this app's users often cannot reach. Free accounts carry no expiry at all and
        // are the many: every read of theirs must be free.
        var callsAfterSignIn = accountProvider.GetAccountCalls;
        for (var i = 0; i < 5; i++)
            await accountService.GetAccount(CancellationToken.None);

        Assert.AreEqual(callsAfterSignIn, accountProvider.GetAccountCalls,
            "a device that is fine must cost the portal nothing — no launch poll, no recheck clock");
    }

    [TestMethod]
    public async Task Clearing_a_code_while_signed_in_never_empties_the_account_slot()
    {
        var accountCode = TestAppHelper.BuildAccessCode();
        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(accountCode) };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        var profileId = app.ClientProfileService.List().First().ClientProfileId;
        Assert.IsNotNull(app.ClientProfileService.Get(profileId).AccessCode);

        // The app has no way to empty the account's slot: inventory lives in the panel (§5, §7), and
        // there is no Remove at all while signed in. Clearing the profile therefore says nothing to
        // the account, and the very next refresh hands the code straight back.
        TypeAccessCode(app, profileId, null);
        await accountService.Refresh(CancellationToken.None);

        Assert.AreEqual(accountCode, app.ClientProfileService.Get(profileId).AccessCode);
        Assert.IsFalse(accountProvider.SetAccessCodeCalls.Any(x => x == null),
            "nothing in the app may reach for the account's slot");
    }

    [TestMethod]
    public async Task A_device_code_the_server_would_refuse_is_replaced_at_sign_in_and_connects()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var baseToken = TestHelper.CreateAccessToken(server);
        var premiumToken = TestHelper.CreateAccessToken(server, maxClientCount: 6);
        var unknownCode = TestAppHelper.BuildAccessCode();
        var accountCode = BuildDistinctAccessCode(unknownCode);
        accessManager.AccessCodes.Add(accountCode, premiumToken.TokenId);

        var accountProvider = new TestAccountProvider { Account = CreateFreeAccount(accountCode) };
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AccountProvider = accountProvider;
        appOptions.AccessKeys = [baseToken.ToAccessKey()];
        appOptions.Premium = new AppPremiumOptions { AllowImportAccessCode = true };
        await using var app = TestAppHelper.CreateClientApp(appOptions);
        var accountService = GetAccountService(app);

        // This device holds a code the access server has never heard of, and the person chose
        // "sign in without it" at the prompt (§6) — which removes it before the sign-in, so there is
        // nothing to upload and nothing dead left to block a working credential.
        var profileId = app.ClientProfileService.List().First().ClientProfileId;
        TypeAccessCode(app, profileId, unknownCode);
        TypeAccessCode(app, profileId, null);

        await SignIn(accountService);
        await app.Connect(profileId);

        var profile = app.ClientProfileService.Get(profileId);
        Assert.AreEqual(AppConnectionState.Connected, app.ConnectionState);
        Assert.AreEqual(accountCode, profile.AccessCode,
            "sign-in put the account's ranked code on the device, so the dead one never reaches a connection");
        Assert.IsNull(profile.AccessCodeRefusal);
        Assert.IsNull(accountProvider.UploadedAccessCode, "a code removed at the prompt is never uploaded");
    }
}

using System.Net;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class BillingServiceTest : TestAppBase
{
    private static Token CreateToken()
    {
        var randomId = Guid.NewGuid();
        var token = new Token {
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

        return token;
    }

    private VpnHoodApp CreateAppWithAccount(TestAccountProvider accountProvider)
    {
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AccountProvider = accountProvider;
        appOptions.AccessKeys = [CreateToken().ToAccessKey()];
        return TestAppHelper.CreateClientApp(appOptions);
    }

    private static BillingService GetBillingService(VpnHoodApp app)
    {
        return app.Services.AccountService?.BillingService
               ?? throw new InvalidOperationException("BillingService is not available in the test app.");
    }

    private static AccountService GetAccountService(VpnHoodApp app)
    {
        return app.Services.AccountService
               ?? throw new InvalidOperationException("AccountService is not available in the test app.");
    }

    /// <summary>The account the backend serves once the store payment has been turned into an entitlement.</summary>
    private Account CreateSubscribedAccount(DateTime expirationTime, bool isAutoRenew)
    {
        return new Account {
            UserId = Guid.Empty.ToString(),
            Email = "buyer@example.com",
            // the snapshot arrives with the code the subscription delivers, in the same answer
            AccessCodeInfo = new AccessCodeInfo { AccessCode = TestAppHelper.BuildAccessCode() },
            Subscription = new Subscription {
                StoreId = "googleplay",
                CreatedTime = DateTime.UtcNow.AddDays(-2),
                ExpirationTime = expirationTime,
                PriceAmount = 9,
                PriceCurrency = "USD",
                BillingPeriod = "P1M",
                IsAutoRenew = isAutoRenew
            }
        };
    }

    private static Task SignIn(AccountService accountService)
    {
        return accountService.AuthenticationService.SignIn(AppUiContext.RequiredContext,
            new SignInOptions { ProviderId = AuthProviders.Google }, CancellationToken.None);
    }

    [TestMethod]
    public async Task GetSubscriptionPlans_prices_exactly_what_the_account_provider_sells()
    {
        var accountProvider = new TestAccountProvider {
            ProductIds = ["premium_1m", "premium_1y"]
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var billingService = GetBillingService(app);

        // the seam: the account backend answers WHICH products, the store only prices them
        await billingService.GetSubscriptionPlans(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "premium_1m", "premium_1y" },
            accountProvider.TestBillingProvider.LastRequestedProductIds?.ToArray());
    }

    [TestMethod]
    public async Task Purchase_uses_processor_attribution_and_completes_order()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var billingService = GetBillingService(app);

        var purchaseParams = new PurchaseParams { PlanToken = "test_plan_1m" };

        await billingService.Purchase(AppUiContext.RequiredContext, purchaseParams, CancellationToken.None);

        // the store is told whose account pays, and the order processor is the only one who says so
        var lastAttribution = accountProvider.TestBillingProvider.LastAttribution
                              ?? throw new InvalidOperationException("Billing provider did not receive an attribution.");
        Assert.AreEqual(accountProvider.TestOrderProcessor.Attribution.UserId, lastAttribution.UserId);

        // the store's proof is what reaches the backend — and it never travels back to the caller
        Assert.HasCount(1, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual("test_purchase_data", accountProvider.TestOrderProcessor.CompletedOrders[0].Value);
        Assert.AreEqual(PurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task Purchase_is_not_redeemed_when_the_account_changed_while_the_store_answered()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        var billingService = GetBillingService(app);
        await SignIn(accountService);

        // the store's sheet outlives the session that opened it: someone signs out mid-purchase.
        // Whoever is signed in when the store finally answers is not who bought, so the proof must
        // NOT be redeemed here — it stays valid, and Restore presents it again under the right one.
        accountProvider.TestBillingProvider.WhileStoreIsAnswering = () =>
            accountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            billingService.Purchase(AppUiContext.RequiredContext,
                new PurchaseParams { PlanToken = "test_plan_1m" }, CancellationToken.None));

        Assert.IsEmpty(accountProvider.TestOrderProcessor.CompletedOrders,
            "paid access must never land in a stranger's account");
        Assert.AreEqual(PurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task OpenSubscriptionManagement_is_refused_when_the_store_cannot_show_it()
    {
        // The SPA withholds the control in this state, but withholding is not enforcement: a stale
        // page or a fork's own UI can still ask, and the answer must be a refusal with a reason —
        // not a call into a store that opens nothing and reports success.
        var accountProvider = new TestAccountProvider {
            TestBillingProvider = { IsSubscriptionManagementSupported = false }
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var billingService = GetBillingService(app);

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            billingService.OpenSubscriptionManagement(AppUiContext.RequiredContext, CancellationToken.None));

        Assert.IsFalse(accountProvider.TestBillingProvider.WasSubscriptionManagementOpened,
            "the store must never be asked once the app has said it cannot show it");
    }

    [TestMethod]
    public async Task Purchase_rejects_when_already_premium()
    {
        var accountProvider = new TestAccountProvider {
            Account = new Account { UserId = Guid.Empty.ToString(), Subscription = new Subscription { StoreId = "googleplay" } }
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = app.Services.AccountService
                             ?? throw new InvalidOperationException("AccountService is not available in the test app.");

        // sign in so the account (with its active subscription) becomes visible
        await accountService.AuthenticationService.SignIn(AppUiContext.RequiredContext,
            new SignInOptions { ProviderId = AuthProviders.Google }, CancellationToken.None);

        var billingService = GetBillingService(app);
        var purchaseParams = new PurchaseParams { PlanToken = "test_plan_1m" };

        await Assert.ThrowsExactlyAsync<AlreadyExistsException>(() =>
            billingService.Purchase(AppUiContext.RequiredContext, purchaseParams, CancellationToken.None));

        Assert.HasCount(0, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(PurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task Purchase_is_prevented_when_the_account_is_served_by_its_code()
    {
        // The website customer who signs in mid-purchase (lifecycle §8): no store subscription,
        // but the backend serves the account's chosen code — so the account is already premium.
        var accountProvider = new TestAccountProvider {
            Account = new Account {
                UserId = Guid.Empty.ToString(),
                Subscription = null,
                AccessCodeInfo = new AccessCodeInfo { AccessCode = TestAppHelper.BuildAccessCode() }
            }
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        var billingService = GetBillingService(app);
        var purchaseParams = new PurchaseParams { PlanToken = "test_plan_1m" };

        await Assert.ThrowsExactlyAsync<AlreadyExistsException>(() =>
            billingService.Purchase(AppUiContext.RequiredContext, purchaseParams, CancellationToken.None));

        // The claim that matters is WHERE it stopped: before the store's payment sheet. After the
        // sheet the money has moved, and on at least one store nothing refunds it automatically.
        Assert.IsNull(accountProvider.TestBillingProvider.LastPurchaseParams,
            "the store's payment sheet must never open for a served account");
        Assert.HasCount(0, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(PurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task Purchase_failure_resets_state_and_skips_order_completion()
    {
        var accountProvider = new TestAccountProvider {
            TestBillingProvider = {
                PurchaseException = new Exception("The store rejected the purchase.")
            }
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var billingService = GetBillingService(app);

        var purchaseParams = new PurchaseParams { PlanToken = "test_plan_1m" };
        await Assert.ThrowsExactlyAsync<Exception>(() =>
            billingService.Purchase(AppUiContext.RequiredContext, purchaseParams, CancellationToken.None));

        Assert.HasCount(0, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(PurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task RestorePurchase_reports_nothing_when_store_owns_nothing()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var billingService = GetBillingService(app);

        var restored = await billingService.RestorePurchase(AppUiContext.RequiredContext, CancellationToken.None);

        Assert.IsFalse(restored);
        Assert.HasCount(0, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(PurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task RestorePurchase_completes_restored_order()
    {
        var accountProvider = new TestAccountProvider {
            TestBillingProvider = { RestoreResult = new PurchaseProof { Value = "restored_purchase_data" } }
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var billingService = GetBillingService(app);

        var restored = await billingService.RestorePurchase(AppUiContext.RequiredContext, CancellationToken.None);

        Assert.IsTrue(restored);
        Assert.HasCount(1, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual("restored_purchase_data", accountProvider.TestOrderProcessor.CompletedOrders[0].Value);
        Assert.AreEqual(PurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task Purchase_grants_premium_and_delivers_the_access_code()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        var billingService = GetBillingService(app);
        await SignIn(accountService);

        // the store bills, then the backend turns the verified order into the entitlement
        accountProvider.TestOrderProcessor.OnCompleteOrder = _ => {
            accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(30), isAutoRenew: true);
            return Task.CompletedTask;
        };

        Assert.IsFalse(await accountService.HasSubscription(useCache: false, CancellationToken.None),
            "nothing is bought yet");

        await billingService.Purchase(AppUiContext.RequiredContext,
            new PurchaseParams { PlanToken = "test_plan_1m" }, CancellationToken.None);

        Assert.IsTrue(await accountService.HasSubscription(useCache: true, CancellationToken.None),
            "the purchase must be visible without another round trip");

        // the purchase carries its billing terms, or the UI cannot say what is charged, per what, and when
        var account = await accountService.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account);
        Assert.AreEqual(9m, account.Subscription?.PriceAmount);
        Assert.AreEqual("USD", account.Subscription?.PriceCurrency);
        Assert.AreEqual("P1M", account.Subscription?.BillingPeriod);
        Assert.AreEqual(true, account.Subscription?.IsAutoRenew);

        // and the entitlement reaches the connection itself, as an account-sourced access code
        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNotNull(profile.AccessCode, "the subscription's own code is what the backend ranked first");
        Assert.IsTrue(profile.IsPremium);
    }

    [TestMethod]
    public async Task Cancelled_subscription_stays_premium_until_the_expiry_date()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        // the buyer cancelled in the store: auto-renew is off, but the paid period has NOT run out
        var expirationTime = DateTime.UtcNow.AddDays(12);
        accountProvider.Account = CreateSubscribedAccount(expirationTime, isAutoRenew: false);

        Assert.IsTrue(await accountService.HasSubscription(useCache: false, CancellationToken.None),
            "a cancelled subscription is still paid for until it expires");

        var account = await accountService.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account);
        Assert.AreEqual(false, account.Subscription?.IsAutoRenew, "the UI must be able to say 'ends on', not 'renews on'");
        Assert.AreEqual(expirationTime, account.Subscription?.ExpirationTime);
        Assert.AreEqual("googleplay", account.Subscription?.StoreId);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsTrue(profile.IsPremium, "cancelling must not take away the period already bought");

        // ...and it survives an unreachable backend: the cache is trusted while its own expiry is ahead
        var callsBefore = accountProvider.GetAccountCalls;
        accountProvider.Account = null;
        Assert.IsTrue(await accountService.HasSubscription(useCache: true, CancellationToken.None));
        Assert.AreEqual(callsBefore, accountProvider.GetAccountCalls,
            "an unexpired account must not need the network to stay premium");
    }

    [TestMethod]
    public async Task Expired_subscription_leaves_its_code_for_the_server_to_refuse()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(12), isAutoRenew: false);
        Assert.IsTrue(await accountService.HasSubscription(useCache: false, CancellationToken.None));

        // the paid period runs out; with auto-renew off the backend has no entitlement left to serve
        accountProvider.Account = new Account {
            UserId = Guid.Empty.ToString(),
            Email = "buyer@example.com",
            Subscription = null
        };

        Assert.IsFalse(await accountService.HasSubscription(useCache: false, CancellationToken.None),
            "an expired subscription is not premium");

        var account = await accountService.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account, "the person is still signed in, they just have no subscription");
        Assert.IsNull(account.Subscription);
        Assert.IsNull(account.Subscription?.ExpirationTime);

        // The spent code STAYS. Taking it off here would drop ClientProfile.IsPremium and demote the
        // build to its own free edition — premium locations gone, promotion banner back — with nobody
        // told and nobody having chosen it (keyring plan §8). The account is still here; it simply has
        // nothing ranked, which is not the same as being gone.
        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNotNull(profile.AccessCode, "an expired subscription must not silently disarm premium");
        Assert.IsTrue(profile.IsPremium);

        // Leaving it costs nothing, because the access server is the real gate: the spent code is
        // refused at connect time, and THAT is what the person is told — the same road a refusal
        // already takes, with Restore Premium and (where the build allows codes) Change code.
        app.ClientProfileService.MarkAccessCodeRefused(profile.ClientProfileId, SessionErrorCode.AccessExpired);

        profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.AreEqual(SessionErrorCode.AccessExpired, profile.AccessCodeRefusal?.ErrorCode,
            "the refusal is the record that premium ended; no second flag carries the same news");
    }

    [TestMethod]
    public async Task Signing_out_takes_the_account_access_code_with_it()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(30), isAutoRenew: true);
        await accountService.Refresh(CancellationToken.None);
        Assert.IsNotNull(app.CurrentClientProfileInfo?.AccessCode);

        // the user's own choice, and a reversible one: signing in again fetches the code back, while
        // keeping it would carry paid access into whatever account signs in next
        await accountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, CancellationToken.None);

        Assert.IsNull(app.CurrentClientProfileInfo?.AccessCode);
    }

    [TestMethod]
    public async Task An_account_that_disappears_takes_its_access_code_with_it()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(30), isAutoRenew: true);
        await accountService.Refresh(CancellationToken.None);
        Assert.IsNotNull(app.CurrentClientProfileInfo?.AccessCode);

        // the account was deleted on ANOTHER device. A signed-in device holds only account state, so
        // premium stops here too — the entitlement still exists at the store and comes back with
        // Restore Purchase onto a new account.
        accountProvider.Account = null;
        await accountService.Refresh(CancellationToken.None);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNull(profile.AccessCode, "premium must not outlive the account that granted it");
    }

    [TestMethod]
    public async Task Deleting_the_account_takes_premium_with_it()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(30), isAutoRenew: true);
        await accountService.Refresh(CancellationToken.None);
        Assert.IsNotNull(app.CurrentClientProfileInfo?.AccessCode);

        await accountService.DeleteAccount(AppUiContext.RequiredContext, CancellationToken.None);

        Assert.AreEqual(1, accountProvider.DeleteAccountCalls);
        Assert.IsNull(await accountService.GetAccount(CancellationToken.None));
        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNull(profile.AccessCode, "'delete my account' must not leave premium running");
    }

    [TestMethod]
    public async Task Deleting_the_account_takes_a_code_it_had_taken()
    {
        var accountProvider = new TestAccountProvider { Account = new Account { UserId = Guid.Empty.ToString() } };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        // A code uploaded while signed in became the account's (keyring plan §6), so there is nothing
        // to keep back — the person still has it wherever it reached them from. Only a code the
        // account never took stays behind, and this one was taken.
        // Address the profile directly: CurrentClientProfileInfo needs UserSettings.ClientProfileId,
        // which a freshly built test app may not have set yet, while AccountService falls back to the
        // first profile — so asking the app makes this test race its own startup.
        var profileId = app.ClientProfileService.List().First().ClientProfileId;
        app.ClientProfileService.Update(profileId,
            new ClientProfileUpdateParams { AccessCode = new Patch<string?>(TestAppHelper.BuildAccessCode()) });
        await accountService.Refresh(CancellationToken.None);
        Assert.IsNotNull(app.ClientProfileService.Get(profileId).AccessCode);
        Assert.IsTrue(app.ClientProfileService.Get(profileId).IsAccessCodeSynced);

        await accountService.DeleteAccount(AppUiContext.RequiredContext, CancellationToken.None);

        Assert.IsNull(app.ClientProfileService.Get(profileId).AccessCode,
            "the account is gone, and a code it had taken goes with it");
    }

    [TestMethod]
    public async Task Purchase_is_refused_while_a_cancelled_subscription_has_not_expired()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        var billingService = GetBillingService(app);
        await SignIn(accountService);

        accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(12), isAutoRenew: false);

        // the period is still running, so the store would refuse a second subscription anyway
        await Assert.ThrowsExactlyAsync<AlreadyExistsException>(() =>
            billingService.Purchase(AppUiContext.RequiredContext,
                new PurchaseParams { PlanToken = "test_plan_1m" }, CancellationToken.None));

        Assert.HasCount(0, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(PurchaseState.None, billingService.PurchaseState);
    }
}

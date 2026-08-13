using System.Net;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Exceptions;

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

    private static AppBillingService GetBillingService(VpnHoodApp app)
    {
        return app.Services.AccountService?.BillingService
               ?? throw new InvalidOperationException("BillingService is not available in the test app.");
    }

    private static AppAccountService GetAccountService(VpnHoodApp app)
    {
        return app.Services.AccountService
               ?? throw new InvalidOperationException("AccountService is not available in the test app.");
    }

    /// <summary>The account the backend serves once the store payment has been turned into an entitlement.</summary>
    private static AppAccount CreateSubscribedAccount(DateTime expirationTime, bool isAutoRenew)
    {
        return new AppAccount {
            UserId = Guid.Empty.ToString(),
            Email = "buyer@example.com",
            SubscriptionId = "sub_1",
            ProviderPlanId = "premium/monthly",
            ProviderSubscriptionId = "GPA.1111",
            CreatedTime = DateTime.UtcNow.AddDays(-2),
            ExpirationTime = expirationTime,
            PriceAmount = 9,
            PriceCurrency = "USD",
            PriceBillingPeriod = "P1M",
            IsAutoRenew = isAutoRenew
        };
    }

    private static Task SignIn(AppAccountService accountService)
    {
        return accountService.AuthenticationService.SignIn(AppUiContext.RequiredContext,
            new AppSignInOptions { Method = AppSignInMethods.Google }, CancellationToken.None);
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

        // a client-supplied attribution must be overwritten by the order processor
        var purchaseParams = new PurchaseParams {
            PurchaseToken = "test_plan_1m",
            Attribution = new AppPurchaseAttribution { AccountId = "bogus-client-value" }
        };

        var orderId = await billingService.Purchase(AppUiContext.RequiredContext, purchaseParams,
            CancellationToken.None);

        var lastPurchaseParams = accountProvider.TestBillingProvider.LastPurchaseParams
                                 ?? throw new InvalidOperationException("Billing provider did not receive purchase params.");
        Assert.AreEqual(accountProvider.TestOrderProcessor.Attribution.AccountId,
            lastPurchaseParams.Attribution?.AccountId);

        Assert.HasCount(1, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(orderId, accountProvider.TestOrderProcessor.CompletedOrders[0].ProviderOrderId);
        Assert.AreEqual(BillingPurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task Purchase_rejects_when_already_premium()
    {
        var accountProvider = new TestAccountProvider {
            Account = new AppAccount { UserId = Guid.Empty.ToString(), SubscriptionId = "sub_1" }
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = app.Services.AccountService
                             ?? throw new InvalidOperationException("AccountService is not available in the test app.");

        // sign in so the account (with its active subscription) becomes visible
        await accountService.AuthenticationService.SignIn(AppUiContext.RequiredContext,
            new AppSignInOptions { Method = AppSignInMethods.Google }, CancellationToken.None);

        var billingService = GetBillingService(app);
        var purchaseParams = new PurchaseParams { PurchaseToken = "test_plan_1m" };

        await Assert.ThrowsExactlyAsync<AlreadyExistsException>(() =>
            billingService.Purchase(AppUiContext.RequiredContext, purchaseParams, CancellationToken.None));

        Assert.HasCount(0, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(BillingPurchaseState.None, billingService.PurchaseState);
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

        var purchaseParams = new PurchaseParams { PurchaseToken = "test_plan_1m" };
        await Assert.ThrowsExactlyAsync<Exception>(() =>
            billingService.Purchase(AppUiContext.RequiredContext, purchaseParams, CancellationToken.None));

        Assert.HasCount(0, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(BillingPurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task RestorePurchase_returns_null_when_store_has_nothing()
    {
        var accountProvider = new TestAccountProvider();
        await using var app = CreateAppWithAccount(accountProvider);
        var billingService = GetBillingService(app);

        var orderId = await billingService.RestorePurchase(AppUiContext.RequiredContext, CancellationToken.None);

        Assert.IsNull(orderId);
        Assert.HasCount(0, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(BillingPurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task RestorePurchase_completes_restored_order()
    {
        var accountProvider = new TestAccountProvider {
            TestBillingProvider = {
                RestoreResult = new AppPurchaseResult {
                    ProviderOrderId = "restored_order_1"
                }
            }
        };
        await using var app = CreateAppWithAccount(accountProvider);
        var billingService = GetBillingService(app);

        var orderId = await billingService.RestorePurchase(AppUiContext.RequiredContext, CancellationToken.None);

        Assert.AreEqual("restored_order_1", orderId);
        Assert.HasCount(1, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual("restored_order_1", accountProvider.TestOrderProcessor.CompletedOrders[0].ProviderOrderId);
        Assert.AreEqual(BillingPurchaseState.None, billingService.PurchaseState);
    }

    [TestMethod]
    public async Task Purchase_grants_premium_and_delivers_the_access_code()
    {
        var accessCode = TestAppHelper.BuildAccessCode();
        var accountProvider = new TestAccountProvider { AccessCode = accessCode };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        var billingService = GetBillingService(app);
        await SignIn(accountService);

        // the store bills, then the backend turns the verified order into the entitlement
        accountProvider.TestOrderProcessor.OnCompleteOrder = _ => {
            accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(30), isAutoRenew: true);
            return Task.CompletedTask;
        };

        Assert.IsFalse(await accountService.IsPremium(useCache: false, CancellationToken.None),
            "nothing is bought yet");

        var orderId = await billingService.Purchase(AppUiContext.RequiredContext,
            new PurchaseParams { PurchaseToken = "test_plan_1m" }, CancellationToken.None);

        Assert.IsNotNull(orderId);
        Assert.IsTrue(await accountService.IsPremium(useCache: true, CancellationToken.None),
            "the purchase must be visible without another round trip");

        // the purchase carries its billing terms, or the UI cannot say what is charged, per what, and when
        var account = await accountService.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account);
        Assert.AreEqual("premium/monthly", account.ProviderPlanId);
        Assert.AreEqual(9m, account.PriceAmount);
        Assert.AreEqual("USD", account.PriceCurrency);
        Assert.AreEqual("P1M", account.PriceBillingPeriod);
        Assert.AreEqual(true, account.IsAutoRenew);

        // and the entitlement reaches the connection itself, as an account-sourced access code
        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsTrue(profile.IsAccessCodeFromAccount, "the code is owned by the account, not typed by the user");
        Assert.IsTrue(profile.IsPremium);
    }

    [TestMethod]
    public async Task Cancelled_subscription_stays_premium_until_the_expiry_date()
    {
        var accountProvider = new TestAccountProvider { AccessCode = TestAppHelper.BuildAccessCode() };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        // the buyer cancelled in the store: auto-renew is off, but the paid period has NOT run out
        var expirationTime = DateTime.UtcNow.AddDays(12);
        accountProvider.Account = CreateSubscribedAccount(expirationTime, isAutoRenew: false);

        Assert.IsTrue(await accountService.IsPremium(useCache: false, CancellationToken.None),
            "a cancelled subscription is still paid for until it expires");

        var account = await accountService.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account);
        Assert.AreEqual(false, account.IsAutoRenew, "the UI must be able to say 'ends on', not 'renews on'");
        Assert.AreEqual(expirationTime, account.ExpirationTime);
        Assert.AreEqual("sub_1", account.SubscriptionId);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsTrue(profile.IsPremium, "cancelling must not take away the period already bought");

        // ...and it survives an unreachable backend: the cache is trusted while its own expiry is ahead
        var callsBefore = accountProvider.GetAccountCalls;
        accountProvider.Account = null;
        Assert.IsTrue(await accountService.IsPremium(useCache: true, CancellationToken.None));
        Assert.AreEqual(callsBefore, accountProvider.GetAccountCalls,
            "an unexpired account must not need the network to stay premium");
    }

    [TestMethod]
    public async Task Cancelled_subscription_loses_premium_after_the_expiry_date()
    {
        var accountProvider = new TestAccountProvider { AccessCode = TestAppHelper.BuildAccessCode() };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(12), isAutoRenew: false);
        Assert.IsTrue(await accountService.IsPremium(useCache: false, CancellationToken.None));

        // the paid period runs out; with auto-renew off the backend has no entitlement left to serve
        accountProvider.Account = new AppAccount {
            UserId = Guid.Empty.ToString(),
            Email = "buyer@example.com",
            SubscriptionId = null
        };

        Assert.IsFalse(await accountService.IsPremium(useCache: false, CancellationToken.None),
            "an expired subscription is not premium");

        var account = await accountService.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account, "the person is still signed in, they just have no subscription");
        Assert.IsNull(account.SubscriptionId);
        Assert.IsNull(account.ExpirationTime);

        // The code the subscription delivered is spent, so it comes off the profile. Left there it
        // would hold the LOCAL premium gate open (always-on, custom DNS, split tunneling…) — because
        // ClientProfile.IsPremium is "AccessCode != null" — long after the server stopped honouring it.
        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsFalse(profile.IsAccessCodeFromAccount);
        Assert.IsNull(profile.AccessCode, "an expired subscription leaves no access code behind");
    }

    [TestMethod]
    public async Task Signing_out_takes_the_account_access_code_with_it()
    {
        var accountProvider = new TestAccountProvider { AccessCode = TestAppHelper.BuildAccessCode() };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(30), isAutoRenew: true);
        await accountService.Refresh(CancellationToken.None);
        Assert.IsTrue(app.CurrentClientProfileInfo?.IsAccessCodeFromAccount);

        // the user's own choice, and a reversible one: signing in again fetches the code back, while
        // keeping it would carry paid access into whatever account signs in next
        await accountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, CancellationToken.None);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsNull(profile.AccessCode);
        Assert.IsFalse(profile.IsAccessCodeFromAccount);
    }

    [TestMethod]
    public async Task An_account_that_disappears_leaves_the_paid_access_code_on_this_device()
    {
        var accountProvider = new TestAccountProvider { AccessCode = TestAppHelper.BuildAccessCode() };
        await using var app = CreateAppWithAccount(accountProvider);
        var accountService = GetAccountService(app);
        await SignIn(accountService);

        accountProvider.Account = CreateSubscribedAccount(DateTime.UtcNow.AddDays(30), isAutoRenew: true);
        await accountService.Refresh(CancellationToken.None);
        Assert.IsTrue(app.CurrentClientProfileInfo?.IsAccessCodeFromAccount);

        // the account was deleted on ANOTHER device: nobody here asked for anything, and the paid
        // period is still running, so the code is detached into a manual one instead of removed —
        // it could never be fetched again
        accountProvider.Account = null;
        await accountService.Refresh(CancellationToken.None);

        var profile = app.CurrentClientProfileInfo;
        Assert.IsNotNull(profile);
        Assert.IsFalse(profile.IsAccessCodeFromAccount, "there is no account left to own it");
        Assert.IsNotNull(profile.AccessCode, "the wrong device must not lose what was already bought");

        // ...and a later clean sign-out cannot take it away either: by then it is the user's own code
        await accountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, CancellationToken.None);
        Assert.IsNotNull(app.CurrentClientProfileInfo?.AccessCode,
            "signing out of an account that no longer exists must not revoke a detached code");
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
                new PurchaseParams { PurchaseToken = "test_plan_1m" }, CancellationToken.None));

        Assert.HasCount(0, accountProvider.TestOrderProcessor.CompletedOrders);
        Assert.AreEqual(BillingPurchaseState.None, billingService.PurchaseState);
    }
}

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
            new AppSignInOptions { Method = AppSignInMethod.Google }, CancellationToken.None);

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
}

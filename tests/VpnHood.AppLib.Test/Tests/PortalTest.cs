using System.Security.Authentication;
using System.Text.Json;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Portal;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.Test.Tests;

/// <summary>
/// VpnHood.AppLib.Portal against a scripted loopback portal: the real HTTP
/// surface (routes, verbs, bodies, bearer headers, session persistence) with
/// no real backend and no store.
/// </summary>
[TestClass]
public class PortalTest
{
    private class TestUiContext : IUiContext
    {
        public Task<bool> IsDestroyed() => Task.FromResult(false);
        public Task<bool> IsActive() => Task.FromResult(true);
    }

    private const string PackageName = "com.vpnhood.connect.android";
    private const string SignInRoute = "POST /auth/sessions";
    private const string SignOutRoute = "DELETE /auth/sessions/current";
    private static readonly Guid ExternalUid = Guid.Parse("c0ffee00-0000-4000-8000-000000000001");

    private TestPortalServer _portal = null!;
    private string _storageFolder = null!;

    [TestInitialize]
    public void Initialize()
    {
        _portal = new TestPortalServer();
        _storageFolder = Path.Combine(Path.GetTempPath(), "vhtest-portal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageFolder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _portal.Dispose();
        if (Directory.Exists(_storageFolder))
            Directory.Delete(_storageFolder, recursive: true);
    }

    private static object SignInData(string accessToken = "session-token-1")
    {
        return new {
            accessToken,
            expiresAt = DateTime.UtcNow.AddDays(30).ToString("O"),
            userId = ExternalUid.ToString(),
            account = new { email = "buyer@example.com" }
        };
    }

    private PortalAuthenticationProvider CreateAuthenticationProvider(
        params IAppAuthenticationExternalProvider[] externalProviders)
    {
        return new PortalAuthenticationProvider(
            _storageFolder,
            _portal.BaseUrl,
            PackageName,
            externalProviders.Length > 0
                ? externalProviders
                : [new TestAuthenticationExternalProvider("google-id-token")]);
    }

    [TestMethod]
    public async Task SignIn_posts_the_id_token_and_persists_the_session()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        using var authenticationProvider = CreateAuthenticationProvider();

        Assert.IsNull(authenticationProvider.UserId, "no session before sign-in");
        await authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = AppSignInMethods.Google },
            CancellationToken.None);

        Assert.AreEqual(ExternalUid.ToString(), authenticationProvider.UserId);
        var request = _portal.Requests.Single(x => x.Route == SignInRoute);
        Assert.AreEqual("google", request.Body.GetProperty("provider").GetString());
        Assert.AreEqual("google-id-token", request.Body.GetProperty("idToken").GetString());
        Assert.AreEqual(PackageName, request.Body.GetProperty("packageName").GetString());

        // session file persisted → a NEW provider instance is already signed in
        using var reloadedProvider = CreateAuthenticationProvider();
        Assert.AreEqual(ExternalUid.ToString(), reloadedProvider.UserId, "session must survive restarts");
    }

    [TestMethod]
    public async Task Requests_carry_the_session_as_bearer_and_portal_token()
    {
        _portal.Enqueue(SignInRoute, SignInData(accessToken: "tok-abc"));
        _portal.Enqueue("GET /account", new {
            userId = ExternalUid.ToString(),
            account = new { email = "buyer@example.com" }
        });
        _portal.Enqueue("GET /account/entitlements", new { items = Array.Empty<object>() });

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = AppSignInMethods.Google },
            CancellationToken.None);

        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            PortalStoreIds.GooglePlay, PackageName, fallbackProductIds: []);
        var account = await accountProvider.GetAccount(CancellationToken.None);

        Assert.IsNotNull(account);
        Assert.AreEqual("buyer@example.com", account.Email);
        Assert.IsNull(account.SubscriptionId, "no entitlement → no subscription");

        var request = _portal.Requests.Single(x => x.Route == "GET /account");
        Assert.AreEqual("Bearer tok-abc", request.Authorization);
        Assert.AreEqual("tok-abc", request.PortalToken);
    }

    [TestMethod]
    public async Task GetAccount_maps_the_entitlement_and_GetAccessCode_returns_its_code()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        var entitlementData = new {
            items = new object[] {
                new {
                    state = "provisioned",
                    accessCode = "12345678901234567890",
                    expiresAt = DateTime.UtcNow.AddDays(30).ToString("O"),
                    planId = "vh_premium/monthly",
                    store = PortalStoreIds.GooglePlay
                }
            }
        };
        _portal.Enqueue("GET /account", new {
            userId = ExternalUid.ToString(),
            account = new { email = "buyer@example.com" }
        });
        _portal.Enqueue("GET /account/entitlements", entitlementData);
        _portal.Enqueue("GET /account/entitlements", entitlementData);

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = AppSignInMethods.Google },
            CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            PortalStoreIds.GooglePlay, PackageName, fallbackProductIds: []);

        var account = await accountProvider.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account);
        Assert.AreEqual(PortalAccountProvider.PortalSubscriptionId, account.SubscriptionId);
        Assert.AreEqual("vh_premium/monthly", account.ProviderPlanId);
        Assert.IsNotNull(account.ExpirationTime);
        Assert.IsNull(account.SubscriptionManagementUrl, "no billing provider → no page to offer");

        var accessCode = await accountProvider.GetAccessCode(PortalAccountProvider.PortalSubscriptionId,
            CancellationToken.None);
        Assert.AreEqual("12345678901234567890", accessCode);
    }

    [TestMethod]
    public async Task OrderProcessor_prepares_attribution_and_verifies_the_purchase()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue("POST /billing/purchases", new {
            state = "provisioned",
            accessCode = "12345678901234567890",
            expiresAt = DateTime.UtcNow.AddDays(30).ToString("O"),
            planId = "vh_premium/monthly"
        });

        using var authenticationProvider = CreateAuthenticationProvider();
        using var accountProvider = new PortalAccountProvider(authenticationProvider,
            new TestBillingProvider(), PortalStoreIds.GooglePlay, PackageName, fallbackProductIds: []);
        var orderProcessor = accountProvider.Billing?.OrderProcessor
            ?? throw new InvalidOperationException("Billing must exist when a provider is given.");

        // not signed in → PreparePurchase refuses
        await Assert.ThrowsExactlyAsync<AuthenticationException>(() =>
            orderProcessor.PreparePurchase(CancellationToken.None));

        await authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = AppSignInMethods.Google },
            CancellationToken.None);

        var attribution = await orderProcessor.PreparePurchase(CancellationToken.None);
        Assert.AreEqual(ExternalUid.ToString(), attribution.AccountId);
        Assert.AreEqual(ExternalUid, attribution.AppAccountToken, "the uid doubles as the Apple appAccountToken");

        await orderProcessor.CompleteOrder(
            new AppPurchaseResult { ProviderOrderId = "GPA.1111", PurchaseData = "purchase-token-xyz" },
            CancellationToken.None);

        var request = _portal.Requests.Single(x => x.Route == "POST /billing/purchases");
        Assert.AreEqual(PortalStoreIds.GooglePlay, request.Body.GetProperty("store").GetString());
        Assert.AreEqual(PackageName, request.Body.GetProperty("packageName").GetString());
        Assert.AreEqual("purchase-token-xyz",
            request.Body.GetProperty("proof").GetProperty("purchaseToken").GetString());
    }

    [TestMethod]
    public async Task A_problem_response_becomes_the_standard_ApiException()
    {
        _portal.Enqueue(SignInRoute, new TestPortalServer.ErrorScript {
            Code = "invalid_id_token",
            Detail = "Invalid sign-in token.",
            StatusCode = 401
        });
        using var authenticationProvider = CreateAuthenticationProvider();

        var exception = await Assert.ThrowsExactlyAsync<ApiException>(() =>
            authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = AppSignInMethods.Google },
                CancellationToken.None));
        Assert.AreEqual(401, exception.StatusCode, "the real HTTP status, not ApiError's 400 default");
        Assert.AreEqual("invalid_id_token", exception.Data["Code"], "clients branch on the machine code");
        Assert.AreEqual("Invalid sign-in token.", exception.Message, "the problem detail, no raw response appended");
    }

    [TestMethod]
    public async Task SignOut_revokes_the_session_and_deletes_the_file()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue(SignOutRoute, TestPortalServer.NoContent);

        var externalProvider = new TestAuthenticationExternalProvider("google-id-token");
        using var authenticationProvider = CreateAuthenticationProvider(externalProvider);
        await authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = AppSignInMethods.Google },
            CancellationToken.None);
        Assert.IsNotNull(authenticationProvider.UserId);

        await authenticationProvider.SignOut(new TestUiContext(), CancellationToken.None);

        Assert.IsNull(authenticationProvider.UserId);
        Assert.AreEqual(1, externalProvider.SignOutCalls);
        var signOut = _portal.Requests.Single(x => x.Route == SignOutRoute);
        Assert.AreEqual(JsonValueKind.Undefined, signOut.Body.ValueKind,
            "a DELETE must not carry a body — some servers and proxies reject one");
        using var reloadedProvider = CreateAuthenticationProvider();
        Assert.IsNull(reloadedProvider.UserId, "the session file must be gone");
    }

    [TestMethod]
    public async Task DeleteAccount_erases_the_account_and_this_device_forgets_it()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue("DELETE /account", TestPortalServer.NoContent);
        _portal.Enqueue(SignOutRoute, TestPortalServer.NoContent);

        var externalProvider = new TestAuthenticationExternalProvider("google-id-token");
        var authenticationProvider = CreateAuthenticationProvider(externalProvider);
        await authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = AppSignInMethods.Google },
            CancellationToken.None);

        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            PortalStoreIds.GooglePlay, PackageName, fallbackProductIds: []);
        await accountProvider.DeleteAccount(new TestUiContext(), CancellationToken.None);

        Assert.IsNotNull(_portal.Requests.SingleOrDefault(x => x.Route == "DELETE /account"));
        Assert.IsNull(authenticationProvider.UserId, "this device must be signed out");
        Assert.AreEqual(1, externalProvider.SignOutCalls,
            "the IdP credential must be dropped so the next sign-in is a deliberate act");
        using var reloadedProvider = CreateAuthenticationProvider();
        Assert.IsNull(reloadedProvider.UserId, "the session file must be gone");
    }

    [TestMethod]
    public async Task DeleteAccount_blocked_by_web_services_keeps_the_session()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue("DELETE /account", new TestPortalServer.ErrorScript {
            Code = "deletion_blocked",
            Detail = "This account has active web services. Cancel them in the web client area first, then delete the account.",
            StatusCode = 409
        });

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = AppSignInMethods.Google },
            CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            PortalStoreIds.GooglePlay, PackageName, fallbackProductIds: []);

        var exception = await Assert.ThrowsExactlyAsync<ApiException>(() =>
            accountProvider.DeleteAccount(new TestUiContext(), CancellationToken.None));

        Assert.AreEqual(409, exception.StatusCode);
        Assert.AreEqual("deletion_blocked", exception.Data["Code"], "clients branch on the machine code");
        Assert.IsNotNull(authenticationProvider.UserId,
            "a refused deletion must leave the session intact — the user needs it to come back and retry");
    }

    [TestMethod]
    public async Task GetAccount_offers_the_manage_page_only_when_this_store_billed_it()
    {
        object EntitlementData(string store) => new {
            items = new object[] {
                new {
                    state = "provisioned",
                    accessCode = "12345678901234567890",
                    expiresAt = DateTime.UtcNow.AddDays(30).ToString("O"),
                    store
                }
            }
        };
        object AccountData() => new {
            userId = ExternalUid.ToString(),
            account = new { email = "buyer@example.com" }
        };
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue("GET /account", AccountData());
        _portal.Enqueue("GET /account/entitlements", EntitlementData(PortalStoreIds.GooglePlay));
        _portal.Enqueue("GET /account", AccountData());
        _portal.Enqueue("GET /account/entitlements", EntitlementData(PortalStoreIds.AppStore));

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = AppSignInMethods.Google },
            CancellationToken.None);
        var billingProvider = new TestBillingProvider();
        using var accountProvider = new PortalAccountProvider(authenticationProvider, billingProvider,
            PortalStoreIds.GooglePlay, PackageName, fallbackProductIds: []);

        // this build's store billed the subscription → its manage page is offered
        var account = await accountProvider.GetAccount(CancellationToken.None);
        Assert.AreEqual(billingProvider.SubscriptionManagementUrl, account?.SubscriptionManagementUrl);

        // another store billed it → premium still works, but there is no page this device can open
        account = await accountProvider.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account);
        Assert.AreEqual(PortalAccountProvider.PortalSubscriptionId, account.SubscriptionId,
            "a cross-store subscription must stay premium");
        Assert.IsNull(account.SubscriptionManagementUrl);
    }

    [TestMethod]
    public async Task SignIn_selects_the_external_provider_by_the_method_id()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue(SignOutRoute, TestPortalServer.NoContent);

        var googleProvider = new TestAuthenticationExternalProvider("google-id-token");
        var appleProvider = new TestAuthenticationExternalProvider("apple-id-token", AppSignInMethods.Apple);
        using var authenticationProvider = CreateAuthenticationProvider(googleProvider, appleProvider);

        CollectionAssert.AreEqual(new[] { AppSignInMethods.Google, AppSignInMethods.Apple },
            authenticationProvider.SignInMethods.ToArray(), "every wired provider must be advertised, in order");

        // an id no wired provider declares is refused before any token or portal call
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            authenticationProvider.SignIn(new TestUiContext(), new AppSignInOptions { Method = "github" },
                CancellationToken.None));

        await authenticationProvider.SignIn(new TestUiContext(),
            new AppSignInOptions { Method = AppSignInMethods.Apple }, CancellationToken.None);

        Assert.AreEqual(0, googleProvider.SignInCalls, "only the selected provider may be asked for a token");
        Assert.AreEqual(1, appleProvider.SignInCalls);
        var request = _portal.Requests.Single(x => x.Route == SignInRoute);
        Assert.AreEqual("apple", request.Body.GetProperty("provider").GetString());
        Assert.AreEqual("apple-id-token", request.Body.GetProperty("idToken").GetString());

        // sign-out targets the provider that established the session, not the bystanders
        await authenticationProvider.SignOut(new TestUiContext(), CancellationToken.None);
        Assert.AreEqual(0, googleProvider.SignOutCalls);
        Assert.AreEqual(1, appleProvider.SignOutCalls);
    }

    private IAppProductCatalog CreateProductCatalog(PortalAuthenticationProvider authenticationProvider,
        params string[] fallbackProductIds)
    {
        // the catalog is the account provider's, built from the same store+app it reconciles orders for
        var accountProvider = new PortalAccountProvider(authenticationProvider, new TestBillingProvider(),
            PortalStoreIds.GooglePlay, PackageName, fallbackProductIds);
        return accountProvider.Billing?.ProductCatalog
               ?? throw new InvalidOperationException("Billing must expose a catalog.");
    }

    [TestMethod]
    public async Task ProductCatalog_asks_the_portal_and_deduplicates_the_store_products()
    {
        _portal.Enqueue("GET /billing/plans", new {
            items = new object[] {
                new { planId = "premium/monthly", storeProductId = "premium", basePlanId = "monthly" },
                new { planId = "premium/yearly", storeProductId = "premium", basePlanId = "yearly" },
                new { planId = "premium_plus", storeProductId = "premium_plus", basePlanId = "" }
            }
        });

        using var authenticationProvider = CreateAuthenticationProvider();
        var catalog = CreateProductCatalog(authenticationProvider, "embedded-only");

        var productIds = await catalog.GetProductIds(CancellationToken.None);

        // the store is queried per product, so the two base plans of one product must collapse to one id
        CollectionAssert.AreEqual(new[] { "premium", "premium_plus" }, productIds.ToArray());

        // the catalog is public: it is read without a session, and the app+store select the rows
        var request = _portal.Requests.Single(x => x.Route == "GET /billing/plans");
        Assert.IsNull(request.Authorization, "the plans page renders before anyone signs in");
        StringAssert.Contains(request.Query, $"store={PortalStoreIds.GooglePlay}");
        StringAssert.Contains(request.Query, $"packageName={PackageName}");
    }

    [TestMethod]
    public async Task ProductCatalog_falls_back_to_the_embedded_ids_when_the_portal_cannot_answer()
    {
        _portal.Enqueue("GET /billing/plans",
            new TestPortalServer.ErrorScript { Code = "not_found", Detail = "no portal here", StatusCode = 404 });

        using var authenticationProvider = CreateAuthenticationProvider();
        var catalog = CreateProductCatalog(authenticationProvider,
            "vpnhood_1_month_subscription", "vpnhood_1_year_subscription");

        // an unreachable or outdated portal must not empty the plans page
        var productIds = await catalog.GetProductIds(CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "vpnhood_1_month_subscription", "vpnhood_1_year_subscription" },
            productIds.ToArray());

        // ...but an answer of "nothing is sellable" is an answer, not a failure: falling back there would
        // offer products the portal cannot redeem
        _portal.Enqueue("GET /billing/plans", new { items = Array.Empty<object>() });
        Assert.AreEqual(0, (await catalog.GetProductIds(CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task The_store_prices_exactly_what_the_catalog_sells()
    {
        _portal.Enqueue("GET /billing/plans", new {
            items = new object[] {
                new { planId = "premium", storeProductId = "premium", basePlanId = "" }
            }
        });

        using var authenticationProvider = CreateAuthenticationProvider();
        var billingProvider = new TestBillingProvider();
        using var accountProvider = new PortalAccountProvider(authenticationProvider, billingProvider,
            PortalStoreIds.GooglePlay, PackageName, fallbackProductIds: ["never-used"]);
        var billing = accountProvider.Billing ?? throw new InvalidOperationException("Billing is required.");

        // the seam this design exists for: the backend answers WHICH, the store only prices them
        var productIds = await billing.ProductCatalog.GetProductIds(CancellationToken.None);
        await billing.Provider.GetSubscriptionPlans(productIds, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "premium" }, billingProvider.LastRequestedProductIds?.ToArray());
    }

    [TestMethod]
    public void Providers_with_duplicate_method_ids_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => {
            using var _ = CreateAuthenticationProvider(
                new TestAuthenticationExternalProvider("token-1"),
                new TestAuthenticationExternalProvider("token-2"));
        });
    }
}

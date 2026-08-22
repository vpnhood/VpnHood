using System.Security.Authentication;
using System.Text.Json;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
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

    /// <summary>The sign-in answer: identity and lifetime only — who the person is comes from GET /account.</summary>
    private static object SignInData(string accessToken = "session-token-1")
    {
        return new {
            accessToken,
            expiresAt = DateTime.UtcNow.AddDays(30).ToString("O"),
            userId = ExternalUid.ToString()
        };
    }

    /// <summary>The GET /account snapshot — the wire maps Account 1:1.</summary>
    private static object SnapshotData(object? accessCodeInfo = null, object? subscription = null, string? name = null)
    {
        return new {
            userId = ExternalUid.ToString(),
            name,
            email = "buyer@example.com",
            accessCodeInfo,
            subscription
        };
    }

    private PortalAuthenticationProvider CreateAuthenticationProvider(
        params IAuthenticationExternalProvider[] externalProviders)
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
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
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
        _portal.Enqueue("GET /account", SnapshotData());

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);

        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            _portal.BaseUrl, PackageName);
        var account = await accountProvider.GetAccount(CancellationToken.None);

        Assert.IsNotNull(account);
        Assert.AreEqual("buyer@example.com", account.Email);
        Assert.IsNull(account.Subscription, "nothing store-billed → no subscription");

        var request = _portal.Requests.Single(x => x.Route == "GET /account");
        Assert.AreEqual("Bearer tok-abc", request.Authorization);
        Assert.AreEqual("tok-abc", request.PortalToken);
    }

    [TestMethod]
    public async Task GetAccount_maps_the_snapshot_one_to_one()
    {
        var expiresAt = DateTime.UtcNow.AddDays(30);
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue("GET /account", SnapshotData(
            name: "Alex Smith",
            accessCodeInfo: new { accessCode = "12345678901234567890", expirationTime = expiresAt.ToString("O") },
            subscription: new {
                storeId = StoreIds.GooglePlay,
                createdTime = DateTime.UtcNow.AddDays(-2).ToString("O"),
                expirationTime = expiresAt.ToString("O"),
                priceAmount = 9.99,
                priceCurrency = "USD",
                billingPeriod = "P1M",
                isAutoRenew = true
            }));

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            _portal.BaseUrl, PackageName);

        // one wire read IS the app model — nothing is re-ranked or re-assembled on this side
        var account = await accountProvider.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account);
        Assert.AreEqual(ExternalUid.ToString(), account.UserId);
        Assert.AreEqual("Alex Smith", account.Name);
        Assert.AreEqual("buyer@example.com", account.Email);
        Assert.AreEqual("12345678901234567890", account.AccessCodeInfo?.AccessCode,
            "THE one ranked code rides along with the account — one read, not two");
        Assert.IsNotNull(account.AccessCodeInfo?.ExpirationTime, "the code carries its own clock");
        Assert.AreEqual(StoreIds.GooglePlay, account.Subscription?.StoreId);
        Assert.AreEqual(9.99m, account.Subscription?.PriceAmount);
        Assert.AreEqual("USD", account.Subscription?.PriceCurrency);
        Assert.AreEqual("P1M", account.Subscription?.BillingPeriod);
        Assert.AreEqual(true, account.Subscription?.IsAutoRenew);
        Assert.IsNotNull(account.Subscription?.ExpirationTime);
        Assert.IsNotNull(account.Subscription?.CreatedTime);
        Assert.AreEqual(SubscriptionManagement.AnotherStore, account.Subscription?.Management,
            "no billing provider → nothing here can manage it, and no store may be named");
    }

    [TestMethod]
    public async Task OrderProcessor_prepares_attribution_and_verifies_the_purchase()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        // the purchase answer IS the state — the delivered code lives on GET /account
        _portal.Enqueue("POST /billing/purchases", "provisioned");

        using var authenticationProvider = CreateAuthenticationProvider();
        using var accountProvider = new PortalAccountProvider(authenticationProvider,
            new TestBillingProvider(), _portal.BaseUrl, PackageName);
        var orderProcessor = accountProvider.Billing?.OrderProcessor
            ?? throw new InvalidOperationException("Billing must exist when a provider is given.");

        // not signed in → PreparePurchase refuses
        await Assert.ThrowsExactlyAsync<AuthenticationException>(() =>
            orderProcessor.PreparePurchase(CancellationToken.None));

        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);

        var attribution = await orderProcessor.PreparePurchase(CancellationToken.None);
        // one value for every store: the portal's uid, a UUID so Apple can take it as its
        // appAccountToken without the abstraction carrying a field per store
        Assert.AreEqual(ExternalUid.ToString(), attribution.UserId);
        Assert.IsTrue(Guid.TryParse(attribution.UserId, out _), "every store needs it parseable as a UUID");

        await orderProcessor.CompleteOrder(new PurchaseProof { Value = "purchase-token-xyz" },
            CancellationToken.None);

        var request = _portal.Requests.Single(x => x.Route == "POST /billing/purchases");
        Assert.AreEqual(StoreIds.GooglePlay, request.Body.GetProperty("storeId").GetString());
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
            authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
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
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
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
    public async Task DeleteAccount_erases_the_account()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue("DELETE /account", TestPortalServer.NoContent);

        var externalProvider = new TestAuthenticationExternalProvider("google-id-token");
        var authenticationProvider = CreateAuthenticationProvider(externalProvider);
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);

        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            _portal.BaseUrl, PackageName);
        await accountProvider.DeleteAccount(CancellationToken.None);

        Assert.IsNotNull(_portal.Requests.SingleOrDefault(x => x.Route == "DELETE /account"));
        // signing this device out is AccountService's half of the act — see
        // AccountAccessCodeTest.Deletion_signs_this_device_out; the provider only erases the person,
        // so a refusal (below) can leave the session exactly as it found it
        Assert.IsNotNull(authenticationProvider.UserId);
        Assert.AreEqual(0, externalProvider.SignOutCalls);
    }

    [TestMethod]
    public async Task SetAccessCode_uses_one_put_for_a_code_and_null_removal()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        // the answer carries nothing: what the account serves afterwards is read from GET /account
        _portal.Enqueue("PUT /account/access-code", new { });
        _portal.Enqueue("PUT /account/access-code", new { });

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(),
            new SignInOptions { ProviderId = AuthProviders.Google }, CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            _portal.BaseUrl, PackageName);

        await accountProvider.SetAccessCode("TEST-CODE", CancellationToken.None);
        await accountProvider.SetAccessCode(null, CancellationToken.None);

        var requests = _portal.Requests.Where(x => x.Route == "PUT /account/access-code").ToArray();
        Assert.AreEqual("TEST-CODE", requests[0].Body.GetProperty("accessCode").GetString());
        Assert.AreEqual(JsonValueKind.Null, requests[1].Body.GetProperty("accessCode").ValueKind,
            "null is the resource's explicit empty value; there is no separate remove endpoint");
        Assert.IsFalse(requests[0].Body.TryGetProperty("modifiedTime", out _),
            "nothing is ordered by time: whichever upload arrives last wins, and that is the whole protocol");
    }

    [TestMethod]
    public async Task ReportAccessCodeRejected_sends_the_code_in_the_body_and_nothing_else()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue("POST /account/access-code/rejected", new { });

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(),
            new SignInOptions { ProviderId = AuthProviders.Google }, CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            _portal.BaseUrl, PackageName);

        await accountProvider.ReportAccessCodeRejected("12345678901234567890", CancellationToken.None);

        var request = _portal.Requests.Single(x => x.Route == "POST /account/access-code/rejected");
        Assert.AreEqual("12345678901234567890", request.Body.GetProperty("accessCode").GetString());
        Assert.DoesNotContain("12345678901234567890", request.Route,
            "a bearer credential must never appear in a path: URLs are logged, cached and proxied");
        foreach (var absent in new[] { "expirationTime", "reason", "errorCode", "observedTime", "revision" })
            Assert.IsFalse(request.Body.TryGetProperty(absent, out _),
                $"eligibility is one bit: '{absent}' is exactly the machinery this replaced");
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
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            _portal.BaseUrl, PackageName);

        var exception = await Assert.ThrowsExactlyAsync<ApiException>(() =>
            accountProvider.DeleteAccount(CancellationToken.None));

        Assert.AreEqual(409, exception.StatusCode);
        Assert.AreEqual("deletion_blocked", exception.Data["Code"], "clients branch on the machine code");
        Assert.IsNotNull(authenticationProvider.UserId,
            "a refused deletion must leave the session intact — the user needs it to come back and retry");
    }

    [TestMethod]
    public async Task A_rejected_session_signs_this_device_out()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        // the account was deleted — or its sessions revoked — on another device, so the token this
        // device still holds matches no session row any more
        _portal.Enqueue("GET /account", new TestPortalServer.ErrorScript {
            Code = "unauthorized",
            Detail = "Unauthorized.",
            StatusCode = 401
        });

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            _portal.BaseUrl, PackageName);

        var exception = await Assert.ThrowsExactlyAsync<ApiException>(() =>
            accountProvider.GetAccount(CancellationToken.None));

        Assert.AreEqual(401, exception.StatusCode);
        Assert.IsNull(authenticationProvider.UserId,
            "there is no refresh token, so a rejected session is gone for good");
        using var reloadedProvider = CreateAuthenticationProvider();
        Assert.IsNull(reloadedProvider.UserId, "the session file must be gone, not just the field");
    }

    [TestMethod]
    public async Task A_failing_portal_keeps_the_session()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue("GET /account", new TestPortalServer.ErrorScript {
            Code = "internal_error",
            Detail = "Something went wrong.",
            StatusCode = 500
        });

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, null,
            _portal.BaseUrl, PackageName);

        await Assert.ThrowsExactlyAsync<ApiException>(() => accountProvider.GetAccount(CancellationToken.None));

        Assert.AreEqual(ExternalUid.ToString(), authenticationProvider.UserId,
            "only 401 means 'this is not a session'; a broken backend must never sign anyone out");
    }

    [TestMethod]
    public async Task GetAccount_offers_to_manage_only_when_this_store_billed_it_and_can_show_it()
    {
        object SubscribedSnapshot(string storeId) => SnapshotData(
            accessCodeInfo: new { accessCode = "12345678901234567890", expirationTime = (string?)null },
            subscription: new {
                storeId,
                createdTime = (string?)null,
                expirationTime = DateTime.UtcNow.AddDays(30).ToString("O"),
                priceAmount = (double?)null,
                priceCurrency = (string?)null,
                billingPeriod = (string?)null,
                isAutoRenew = true
            });
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue("GET /account", SubscribedSnapshot(StoreIds.GooglePlay));
        _portal.Enqueue("GET /account", SubscribedSnapshot(StoreIds.AppStore));
        _portal.Enqueue("GET /account", SubscribedSnapshot(StoreIds.GooglePlay));

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);
        var billingProvider = new TestBillingProvider();
        using var accountProvider = new PortalAccountProvider(authenticationProvider, billingProvider,
            _portal.BaseUrl, PackageName);

        // this build's store billed the subscription, and it can show the screen → offered
        var account = await accountProvider.GetAccount(CancellationToken.None);
        Assert.AreEqual(SubscriptionManagement.Available, account?.Subscription?.Management);

        // another store billed it → premium still works, but nothing here can manage it
        account = await accountProvider.GetAccount(CancellationToken.None);
        Assert.IsNotNull(account);
        Assert.AreEqual(StoreIds.AppStore, account.Subscription?.StoreId,
            "a cross-store subscription must stay premium, and must name the store that billed it");
        Assert.AreEqual(SubscriptionManagement.AnotherStore, account.Subscription?.Management,
            "a store this build does not ship to must not be named");

        // the right store, on a device that cannot show its screen — a TV. Distinct from the case
        // above: OUR store billed it, so the UI may name it and send the person to another device.
        billingProvider.IsSubscriptionManagementSupported = false;
        account = await accountProvider.GetAccount(CancellationToken.None);
        Assert.AreEqual(StoreIds.GooglePlay, account?.Subscription?.StoreId);
        Assert.AreEqual(SubscriptionManagement.NotOnThisDevice, account?.Subscription?.Management);
    }

    [TestMethod]
    public async Task GetAccount_maps_a_cancelled_subscription_as_premium_until_it_expires()
    {
        var expiresAt = DateTime.UtcNow.AddDays(12);
        _portal.Enqueue(SignInRoute, SignInData());
        // the store reports the cancellation as isAutoRenew=false; the subscription stands until it expires
        _portal.Enqueue("GET /account", SnapshotData(
            accessCodeInfo: new { accessCode = "12345678901234567890", expirationTime = expiresAt.ToString("O") },
            subscription: new {
                storeId = StoreIds.GooglePlay,
                createdTime = DateTime.UtcNow.AddDays(-18).ToString("O"),
                expirationTime = expiresAt.ToString("O"),
                priceAmount = 9.99,
                priceCurrency = "USD",
                billingPeriod = "P1M",
                isAutoRenew = false
            }));

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, new TestBillingProvider(),
            _portal.BaseUrl, PackageName);

        var account = await accountProvider.GetAccount(CancellationToken.None);

        Assert.IsNotNull(account);
        Assert.AreEqual(StoreIds.GooglePlay, account.Subscription?.StoreId,
            "a cancelled but unexpired subscription is still a subscription");
        Assert.AreEqual(false, account.Subscription?.IsAutoRenew, "the UI must show 'ends on', not 'renews on'");
        Assert.AreEqual(expiresAt.ToString("O"), account.Subscription?.ExpirationTime?.ToUniversalTime().ToString("O"));
        Assert.AreEqual(9.99m, account.Subscription?.PriceAmount);
        Assert.AreEqual("USD", account.Subscription?.PriceCurrency);
        Assert.AreEqual("P1M", account.Subscription?.BillingPeriod);
        Assert.AreEqual(SubscriptionManagement.Available, account.Subscription?.Management,
            "the buyer must still be able to reach the store screen to resubscribe");
    }

    [TestMethod]
    public async Task GetAccount_drops_the_subscription_once_the_portal_serves_none()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        // after the cancelled period ran out the snapshot carries no subscription and no code
        _portal.Enqueue("GET /account", SnapshotData());

        using var authenticationProvider = CreateAuthenticationProvider();
        await authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = AuthProviders.Google },
            CancellationToken.None);
        using var accountProvider = new PortalAccountProvider(authenticationProvider, new TestBillingProvider(),
            _portal.BaseUrl, PackageName);

        var account = await accountProvider.GetAccount(CancellationToken.None);

        Assert.IsNotNull(account, "the person is still signed in");
        Assert.IsNull(account.Subscription, "the expired subscription must not linger");
        Assert.IsNull(account.Subscription?.ExpirationTime);
        Assert.IsNull(account.Subscription?.IsAutoRenew);
    }

    [TestMethod]
    public async Task SignIn_selects_the_external_provider_by_the_method_id()
    {
        _portal.Enqueue(SignInRoute, SignInData());
        _portal.Enqueue(SignOutRoute, TestPortalServer.NoContent);

        var googleProvider = new TestAuthenticationExternalProvider("google-id-token");
        var appleProvider = new TestAuthenticationExternalProvider("apple-id-token", AuthProviders.Apple);
        using var authenticationProvider = CreateAuthenticationProvider(googleProvider, appleProvider);

        CollectionAssert.AreEqual(new[] { AuthProviders.Google, AuthProviders.Apple, AuthProviders.Password },
            authenticationProvider.ProviderIds.ToArray(),
            "every wired provider must be advertised, in order — and the portal's own password form is always appended last");

        // an id no wired provider declares is refused before any token or portal call
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            authenticationProvider.SignIn(new TestUiContext(), new SignInOptions { ProviderId = "github" },
                CancellationToken.None));

        await authenticationProvider.SignIn(new TestUiContext(),
            new SignInOptions { ProviderId = AuthProviders.Apple }, CancellationToken.None);

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

    [TestMethod]
    public async Task SignIn_with_password_reports_the_second_factor_then_completes_it()
    {
        // the same route answers all three password-form calls, in order
        _portal.Enqueue(SignInRoute, new { challenge = new { token = "challenge-1", type = "totp" } });
        _portal.Enqueue(SignInRoute, new {
            accessToken = "session-token-1",
            expiresAt = DateTime.UtcNow.AddDays(30).ToString("O"),
            userId = ExternalUid.ToString(),
            newBackupCode = "backup-2"
        });

        using var authenticationProvider = CreateAuthenticationProvider();

        var challenged = await authenticationProvider.SignIn(new TestUiContext(),
            new SignInOptions {
                ProviderId = AuthProviders.Password,
                UserName = "buyer@example.com",
                Password = "the-password"
            }, CancellationToken.None);

        Assert.AreEqual(SignInState.TotpRequired, challenged.State);
        Assert.IsNull(challenged.NewBackupCode, "a challenge carries no backup code");
        Assert.IsNull(authenticationProvider.UserId, "a challenge signs NOTHING in");

        // the repeat carries only the code — the provider holds the challenge token
        var completed = await authenticationProvider.SignIn(new TestUiContext(),
            new SignInOptions { ProviderId = AuthProviders.Password, TwoFactorCode = "123456" },
            CancellationToken.None);

        Assert.AreEqual(SignInState.SignedIn, completed.State);
        Assert.AreEqual("backup-2", completed.NewBackupCode, "a spent backup code comes back rotated, shown once");
        Assert.AreEqual(ExternalUid.ToString(), authenticationProvider.UserId);

        var completion = _portal.Requests.Last(x => x.Route == SignInRoute);
        Assert.AreEqual("challenge-1", completion.Body.GetProperty("challengeToken").GetString());
        Assert.AreEqual("123456", completion.Body.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task SignIn_with_password_refuses_a_second_factor_this_build_cannot_ask_for()
    {
        _portal.Enqueue(SignInRoute, new { challenge = new { token = "challenge-1", type = "webauthn" } });
        using var authenticationProvider = CreateAuthenticationProvider();

        // a kind no dialog here can prompt for is refused; the alternative is a person stranded on
        // a screen asking the wrong question
        var exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            authenticationProvider.SignIn(new TestUiContext(),
                new SignInOptions {
                    ProviderId = AuthProviders.Password,
                    UserName = "buyer@example.com",
                    Password = "the-password"
                }, CancellationToken.None));

        // the machine code is the UI's contract: without it the dialog can only show raw English
        Assert.AreEqual("unsupported_two_factor", exception.Data["Code"]);

        Assert.IsNull(authenticationProvider.UserId);

        // and the unanswerable challenge was never held
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            authenticationProvider.SignIn(new TestUiContext(),
                new SignInOptions { ProviderId = AuthProviders.Password, TwoFactorCode = "123456" },
                CancellationToken.None));
    }

    [TestMethod]
    public async Task GetProductIds_asks_the_portal_for_the_store_products()
    {
        // the portal answers products, not plans: it has already collapsed the base plans of one
        // product, because the store enumerates those itself and the app cannot act on them
        _portal.Enqueue("GET /billing/products", new[] { "premium", "premium_plus" });

        using var authenticationProvider = CreateAuthenticationProvider();
        using var accountProvider = new PortalAccountProvider(authenticationProvider, new TestBillingProvider(),
            _portal.BaseUrl, PackageName);

        var productIds = await accountProvider.GetProductIds(CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "premium", "premium_plus" }, productIds.ToArray());

        // the catalog is public: it is read without a session, and the app+store select the rows
        var request = _portal.Requests.Single(x => x.Route == "GET /billing/products");
        Assert.IsNull(request.Authorization, "the plans page renders before anyone signs in");
        StringAssert.Contains(request.Query, $"store={StoreIds.GooglePlay}");
        StringAssert.Contains(request.Query, $"packageName={PackageName}");
    }

    [TestMethod]
    public async Task GetProductIds_fails_loudly_when_the_portal_cannot_answer()
    {
        _portal.Enqueue("GET /billing/products",
            new TestPortalServer.ErrorScript { Code = "not_found", Detail = "no portal here", StatusCode = 404 });

        using var authenticationProvider = CreateAuthenticationProvider();
        using var accountProvider = new PortalAccountProvider(authenticationProvider, new TestBillingProvider(),
            _portal.BaseUrl, PackageName);

        // No embedded stand-in: a purchase started against ids the portal cannot confirm would still
        // charge at the store and then have nowhere to be redeemed. The UI turns this into "the store
        // is unavailable, try again", which is the only honest offer while the backend is down.
        await Assert.ThrowsExactlyAsync<ApiException>(() => accountProvider.GetProductIds(CancellationToken.None));

        // ...but an answer of "nothing is sellable" is an answer, not a failure
        _portal.Enqueue("GET /billing/products", Array.Empty<string>());
        Assert.AreEqual(0, (await accountProvider.GetProductIds(CancellationToken.None)).Count);
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

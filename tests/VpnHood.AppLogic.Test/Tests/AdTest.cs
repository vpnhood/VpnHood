using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VpnHood.AppLogic.Api.Exceptions;
using VpnHood.AppLogic.Abstractions.Ads;
using VpnHood.AppLogic.Api.App;
using VpnHood.AppLogic.App;
using VpnHood.AppLogic.App.Services.Ads;
using VpnHood.AppLogic.App.Settings;
using VpnHood.AppLogic.Test.Dom;
using VpnHood.AppLogic.Test.Providers;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using ConnectPlanId = VpnHood.AppLogic.Api.App.ConnectPlanId;
using VpnHood.AppLogic.Abstractions.Ads.AdExceptions;
using VpnHood.AppLogic.Abstractions.Device;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;
using VpnHood.AppLogic.Abstractions.UiContexts;
using AdRoute = VpnHood.Core.Client.Abstractions.AdRoute;

namespace VpnHood.AppLogic.Test.Tests;

[TestClass]
public class AdTest : TestAppBase
{
    [TestMethod]
    public async Task flexible_ad_should_not_close_session_if_load_ad_failed()
    {
        // create server
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create access item
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Flexible);

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProvider = new TestAdProvider(accessManager, AdType.InterstitialAd);
        var adProviderItem = new AppAdProviderItem { AdProvider = adProvider, ProviderName = "UnitTestAd" };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        adProvider.FailLoad = true;
        adProvider.FailShow = true; // should not reach this state

        // connect
        var vpnProfile = app.VpnProfileService.ImportAccessKey(accessManager.GetToken(accessToken).ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
    }

    [TestMethod]
    [DoNotParallelize] // clears the process-global AppUiContext; parallel tests restore it
    public async Task flexible_ad_should_close_session_if_display_ad_failed()
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper, adProviderAdType: AdType.InterstitialAd);
        var app = appDom.App;
        AppUiContext.Context = null;

        // connect
        var token = await appDom.AccessManager.CreateToken(TestCt, adRequirement: AdRequirement.Flexible);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await Assert.ThrowsExactlyAsync<ShowAdNoUiException>(()
            => app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt));
        await app.WaitForState(AppConnectionState.None, TestCt);
    }

    [TestMethod]
    [DoNotParallelize] // clears the process-global AppUiContext; parallel tests restore it
    public async Task flexible_ad_should_not_be_displayed_on_trial()
    {
        // create server
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        AppUiContext.Context = null;
        //adProviderItem.FailShow = true;

        // connect
        var token = await accessManager.CreateToken(TestCt, adRequirement: AdRequirement.Flexible);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, TestCt, planId: ConnectPlanId.PremiumByTrial);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
    }

    [TestMethod]
    [DoNotParallelize] // clears the process-global AppUiContext; parallel tests restore it
    public async Task Session_must_be_closed_after_few_minutes_if_ad_is_not_accepted()
    {
        // create server
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        AppUiContext.Context = null;
        //adProviderItem.FailShow = true;

        // connect
        var token = await accessManager.CreateToken(TestCt);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await Assert.ThrowsExactlyAsync<ShowAdNoUiException>(() => app.Connect(uiContext: null, new ConnectOptions {
            VpnProfileId = vpnProfile.VpnProfileId,
            PlanId = ConnectPlanId.PremiumByRewardedAd
        }, TestCt));

        await app.WaitForState(AppConnectionState.None, TestCt);
    }

    [TestMethod]
    [DoNotParallelize] // clears the process-global AppUiContext; parallel tests restore it
    public async Task An_app_without_ads_refuses_an_ad_server_with_no_window_too()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // a build with no ad network, as the desktop service runs it: with no window
        await using var app = TestAppHelper.CreateClientApp();
        AppUiContext.Context = null;

        // the reason is the app's, not a window that is not open
        var token = await accessManager.CreateToken(TestCt, adRequirement: AdRequirement.Flexible);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        var exception = await Assert.ThrowsExactlyAsync<AdException>(() =>
            app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt));
        StringAssert.Contains(exception.Message, "does not support servers that require ads");
        await app.WaitForState(AppConnectionState.None, TestCt);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RewardedAd_expiration_must_be_increased_by_plan_id(bool acceptAd)
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);

        // create server
        appDom.AccessManager.CanExtendPremiumByAd = true;
        appDom.AccessManager.RejectAllAds = !acceptAd;

        // connect
        if (acceptAd) {
            await appDom.Connect(ConnectPlanId.PremiumByRewardedAd);
            Assert.IsNull(appDom.App.State.SessionStatus?.SessionExpirationTime);
        }
        else {
            var ex = await Assert.ThrowsExactlyAsync<SessionException>(() =>
                appDom.Connect(ConnectPlanId.PremiumByRewardedAd));
            Assert.AreEqual(SessionErrorCode.RewardedAdRejected, ex.SessionResponse.ErrorCode);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task NormalByRewardedAd_must_require_rewarded_ad(bool acceptAd)
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);

        // create server
        appDom.AccessManager.CanExtendPremiumByAd = true;
        appDom.AccessManager.RejectAllAds = !acceptAd;

        // connect: NormalByRewardedAd grants a (free) session in exchange for a rewarded ad
        if (acceptAd) {
            await appDom.Connect(ConnectPlanId.NormalByRewardedAd);
            Assert.IsNull(appDom.App.State.SessionStatus?.SessionExpirationTime);
        }
        else {
            var ex = await Assert.ThrowsExactlyAsync<SessionException>(() =>
                appDom.Connect(ConnectPlanId.NormalByRewardedAd));
            Assert.AreEqual(SessionErrorCode.RewardedAdRejected, ex.SessionResponse.ErrorCode);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RewardedAd_expiration_must_be_increased_by_user(bool acceptAd)
    {
        // create server
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        accessManager.CanExtendPremiumByAd = true;
        accessManager.RejectAllAds = !acceptAd;

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        var device = TestHelper.CreateNullDevice();
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: device);

        // create access token
        var token = await accessManager.CreateToken(TestCt);
        token.IsPublic = true;

        // connect
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, TestCt, ConnectPlanId.PremiumByTrial);

        // assert
        Assert.IsNotNull(app.State.SessionStatus?.SessionExpirationTime);

        // show ad
        if (acceptAd) {
            await app.Api.App.ExtendByRewardedAd(TestCt);
            Assert.IsNull(app.State.SessionStatus?.SessionExpirationTime);
        }
        else {
            var ex = await Assert.ThrowsExactlyAsync<SessionException>(() =>
                app.Api.App.ExtendByRewardedAd(TestCt));
            Assert.AreEqual(SessionErrorCode.RewardedAdRejected, ex.SessionResponse.ErrorCode);
            await Task.Delay(500);
            await app.WaitForState(AppConnectionState.Connected, TestCt);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RewardedAd_should_toggle_by_access_manager(bool enable)
    {
        // create server
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        accessManager.CanExtendPremiumByAd = enable;

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        var device = TestHelper.CreateNullDevice();
        await using var app = TestAppHelper.CreateClientApp(device: device, appOptions: appOptions);

        // create token
        var token = await accessManager.CreateToken(TestCt);
        token.IsPublic = true;

        // connect
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, TestCt, ConnectPlanId.PremiumByTrial);

        // asserts
        Assert.AreEqual(enable, app.State.SessionStatus?.CanExtendByRewardedAd);
    }

    [TestMethod]
    public async Task ShowAd_must_change_state_to_WaitingForAd()
    {
        // create server
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create access item
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Flexible);
        var token = accessManager.GetToken(accessToken);

        // configure client app for ad
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdOptions.LoadAdPostDelay = TimeSpan.FromSeconds(60);
        var adProvider = new TestAdProvider(accessManager, AdType.InterstitialAd);
        var adProviderItem = new AppAdProviderItem { AdProvider = adProvider };
        appOptions.AdProviderItems = [adProviderItem];

        // create client app
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        var isAdLoadingStatusMet = false;
        app.ConnectionStateChanged += (_, _) => {
            if (app.ConnectionState == AppConnectionState.WaitingForAd)
                isAdLoadingStatusMet = true;
        };

        // connect
        _ = app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt); // don't await as it will wait for ad to load
        await app.WaitForState(AppConnectionState.WaitingForAd, TestCt);
        await AssertEqualsWait(true, () => isAdLoadingStatusMet);
    }

    [TestMethod]
    [DoNotParallelize] // reads the process-global test tracker, which every tracker creation clears
    [DataRow(1, "info")]
    [DataRow(5, "warning")]
    public async Task Cancelled_connect_is_info_when_quick_and_a_warning_when_late(int waitSeconds,
        string errorLevel)
    {
        // create server
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // the ad's post-load delay keeps the connect waiting
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Flexible);
        var token = accessManager.GetToken(accessToken);
        var appOptions = TestAppHelper.CreateAppOptions(isDebugMode: false); // a Debug build makes no tracker
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdOptions.LoadAdPostDelay = TimeSpan.FromSeconds(60);
        appOptions.AdProviderItems = [
            new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager, AdType.InterstitialAd) }
        ];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        app.UserSettings.IsLicenseAccepted = true; // as the first-run page does, so the tracker reports
        app.SettingsService.Save();
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        // the person gives up after waiting
        var connectTask = app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.WaitingForAd, TestCt);
        await Task.Delay(TimeSpan.FromSeconds(waitSeconds), TestCt);
        await app.Disconnect();
        await Assert.ThrowsExactlyAsync<UserCanceledException>(() => connectTask);

        // one factory, so the app's tracker is the one it made
        var tracker = (TestTracker)app.Services.TrackerService.Tracker;
        var trackEvent = tracker.FindEvent("vh_exception", "error_type", nameof(UserCanceledException));
        Assert.IsNotNull(trackEvent);
        Assert.AreEqual(errorLevel, trackEvent.Parameters["error_level"]);
        Assert.IsGreaterThanOrEqualTo(waitSeconds, trackEvent.Parameters["elapsed_seconds"] as int? ?? 0);
    }

    [TestMethod]
    public async Task SplitAll_must_on_while_playing_ad_ex()
    {
        using var accessManager = await TestAppHelper.CreateAccessManager();
        await using var server = await TestAppHelper.CreateServer(accessManager: accessManager);

        // create the server
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Flexible);
        var token = accessManager.GetToken(accessToken);

        // add provider
        var showAdCompletionSource = new TaskCompletionSource<ShowAdResult>();
        var adProvider = new TestAdProvider(accessManager, AdType.InterstitialAd);
        adProvider.ShowAdCompletionSource = showAdCompletionSource;
        adProvider.LoadAdCallback = () => {
            if (adProvider.LoadAdCount == 1) // fail first time to use after adapter load
                throw new LoadAdException("Test load failed.");
            return Task.CompletedTask;
        };
        var adProviderItem = new AppAdProviderItem { AdProvider = adProvider };

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.ShowAdPostDelay = TimeSpan.FromSeconds(1);
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdProviderItems = [adProviderItem];

        // create the app
        var device = TestAppHelper.CreateDevice();
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        // we add to exclude but all ip should be split by ad
        _ = app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.WaitingForAd, TestCt);
        await AssertEqualsWait(2, () => adProvider.LoadAdCount);

        // all included ips should be split now
        await FilteringTest.IpFilters_AssertExclude(TestHelper, app, null, MockEps.HttpsUrl1);
        await FilteringTest.IpFilters_AssertInclude(TestHelper, app, MockEps.UdpNsEchoEndPoint1, null);

        // finish showing ad
        // the app deliberately reports Connected during the ad post-delay while the client is still
        // in ad-split mode, so Connected + a fixed delay is not enough; wait for the post-delay to
        // actually end — AdManager clears the flag only after AdOk has been sent to the service
        showAdCompletionSource.SetResult(ShowAdResult.Closed);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
        await AssertEqualsWait(false, () => app.AdManager.IsWaitingForPostDelay);

        // all included ips should not be split now
        await FilteringTest.IpFilters_AssertInclude(TestHelper, app, MockEps.UdpNsEchoEndPoint1, MockEps.HttpsUrl1);

    }

    // A connect's ad: before the VPN, then around the tunnel, then through it where a network can show over the VPN. Only
    // such networks load through the tunnel, and the internal ad only on the last route
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task A_connects_ad_tries_its_routes_in_order_each_with_its_networks(bool isMarked)
    {
        using var accessManager = await TestAppHelper.CreateAccessManager();
        await using var server = await TestAppHelper.CreateServer(accessManager: accessManager);
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Flexible);
        var token = accessManager.GetToken(accessToken);
        VpnHoodClient? client = null;
        var device = new TestDevice(TestAppHelper, _ => new TestVpnAdapter(TestAppHelper.CreateTestVpnAdapterOptions())) {
            ClientCreated = x => client = x
        };

        // the paid networks fail; the internal ad shows
        var unmarkedRoutes = new List<AdRoute?>();
        var markedRoutes = new List<AdRoute?>();
        var internalRoutes = new List<AdRoute?>();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdProviderItems = [
            new AppAdProviderItem {
                AdProvider = CreateRouteRecordingAdProvider(accessManager, unmarkedRoutes, () => client, canShow: false),
                ProviderName = "unmarked"
            },
            new AppAdProviderItem {
                AdProvider = CreateRouteRecordingAdProvider(accessManager, markedRoutes, () => client, canShow: false),
                ProviderName = "marked",
                CanShowOverVpn = isMarked
            },
            new AppAdProviderItem {
                AdProvider = CreateRouteRecordingAdProvider(accessManager, internalRoutes, () => client, canShow: true,
                    isInternal: true),
                ProviderName = "internal",
                CanShowOverVpn = true
            }
        ];

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);

        CollectionAssert.AreEqual(new AdRoute?[] { AdRoute.BeforeVpn, AdRoute.Passthrough }, unmarkedRoutes);
        if (isMarked) {
            CollectionAssert.AreEqual(new AdRoute?[] { AdRoute.BeforeVpn, AdRoute.Passthrough, AdRoute.OverVpn },
                markedRoutes);

            CollectionAssert.AreEqual(new AdRoute?[] { AdRoute.OverVpn }, internalRoutes);
        }
        else {
            CollectionAssert.AreEqual(new AdRoute?[] { AdRoute.BeforeVpn, AdRoute.Passthrough }, markedRoutes);
            CollectionAssert.AreEqual(new AdRoute?[] { AdRoute.Passthrough }, internalRoutes);
        }
    }

    // An internal ad goes through the tunnel only where it is marked, as any network: an unmarked one, as a fork's own
    // may be, loaded there on the last try
    [TestMethod]
    public async Task An_internal_ad_goes_through_the_tunnel_only_where_marked()
    {
        using var accessManager = await TestAppHelper.CreateAccessManager();
        await using var server = await TestAppHelper.CreateServer(accessManager: accessManager);
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Flexible);
        VpnHoodClient? client = null;
        var device = new TestDevice(TestAppHelper, _ => new TestVpnAdapter(TestAppHelper.CreateTestVpnAdapterOptions())) {
            ClientCreated = x => client = x
        };

        var markedRoutes = new List<AdRoute?>();
        var internalRoutes = new List<AdRoute?>();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdProviderItems = [
            new AppAdProviderItem {
                AdProvider = CreateRouteRecordingAdProvider(accessManager, markedRoutes, () => client, canShow: false),
                ProviderName = "marked",
                CanShowOverVpn = true
            },
            new AppAdProviderItem {
                AdProvider = CreateRouteRecordingAdProvider(accessManager, internalRoutes, () => client, canShow: true,
                    isInternal: true),
                ProviderName = "internal"
            }
        ];

        // the flexible ad's last try, through the tunnel, finds no network: the connect goes on without it
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(accessManager.GetToken(accessToken).ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);

        CollectionAssert.AreEqual(new AdRoute?[] { AdRoute.BeforeVpn, AdRoute.Passthrough, AdRoute.OverVpn },
            markedRoutes);

        Assert.IsEmpty(internalRoutes);
    }

    // An ad tries the tunnel only where a network for that kind of ad can show there, where the person is: one marked
    // network of any kind gave every ad a try through the tunnel, an empty one, so the try around it was not its last
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(true, false)]
    public async Task An_ad_tries_the_tunnel_only_where_a_network_for_it_can_show_there(bool isMarked,
        bool isExcludedHere)
    {
        using var accessManager = await TestAppHelper.CreateAccessManager();
        await using var server = await TestAppHelper.CreateServer(accessManager: accessManager);
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Rewarded);
        VpnHoodClient? client = null;
        var device = new TestDevice(TestAppHelper, _ => new TestVpnAdapter(TestAppHelper.CreateTestVpnAdapterOptions())) {
            ClientCreated = x => client = x
        };

        // the rewarded networks fail; one records each try's route and whether it is the last
        var routes = new List<AdRoute?>();
        var lastRoutes = new List<bool>();
        var recordingProvider = new TestAdProvider(accessManager);
        recordingProvider.LoadAdCallback = () => {
            var status = client?.Session?.Status;
            routes.Add(status?.AdRoute);
            lastRoutes.Add(status?.IsLastAdRoute ?? false);
            throw new LoadAdException("The load fails for the test.");
        };

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdProviderItems = [
            new AppAdProviderItem {
                AdProvider = new TestAdProvider(accessManager, AdType.InterstitialAd),
                ProviderName = "interstitial",
                CanShowOverVpn = true
            },
            new AppAdProviderItem { AdProvider = recordingProvider, ProviderName = "recording" },
            new AppAdProviderItem {
                AdProvider = new TestAdProvider(accessManager) { FailLoad = true },
                ProviderName = "rewarded",
                CanShowOverVpn = isMarked,
                ExcludeCountryCodes = isExcludedHere ? [AppRegionInfo.CurrentRegion.Name] : []
            }
        ];

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(accessManager.GetToken(accessToken).ToAccessKey());
        var ex = await Assert.ThrowsAsync<Exception>(() =>
            app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt));

        var isTunnelTried = isMarked && !isExcludedHere;
        CollectionAssert.AreEqual(new AdRoute?[] { AdRoute.BeforeVpn, AdRoute.Passthrough }, routes);
        CollectionAssert.AreEqual(new[] { false, !isTunnelTried }, lastRoutes);

        // the session's last failure comes back as the app's own exception, where it was the service's ApiException
        Assert.AreEqual(typeof(LoadAdException), ex.GetType());
    }

    // A network whose ad loaded but did not show took every try, the networks after it never asked
    [TestMethod]
    public async Task A_network_whose_ad_does_not_show_gives_its_turn_to_the_next()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Flexible);
        var failingProvider = new TestAdProvider(accessManager, AdType.InterstitialAd) { FailShow = true };
        var nextProvider = new TestAdProvider(accessManager, AdType.InterstitialAd);
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdProviderItems = [
            new AppAdProviderItem { AdProvider = failingProvider, ProviderName = "failing" },
            new AppAdProviderItem { AdProvider = nextProvider, ProviderName = "next" }
        ];

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(accessManager.GetToken(accessToken).ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);

        // the first try, before the VPN, shows the next network's ad
        Assert.AreEqual(1, failingProvider.LoadAdCount);
        Assert.AreEqual(1, nextProvider.LoadAdCount);
    }

    // The internal ad, listed before a paid network, took the last try before it
    [TestMethod]
    public async Task The_internal_ad_comes_after_the_paid_networks_wherever_the_head_lists_it()
    {
        using var accessManager = await TestAppHelper.CreateAccessManager();
        await using var server = await TestAppHelper.CreateServer(accessManager: accessManager);
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Flexible);
        VpnHoodClient? client = null;
        var device = new TestDevice(TestAppHelper, _ => new TestVpnAdapter(TestAppHelper.CreateTestVpnAdapterOptions())) {
            ClientCreated = x => client = x
        };

        var internalRoutes = new List<AdRoute?>();
        var paidRoutes = new List<AdRoute?>();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdProviderItems = [
            new AppAdProviderItem {
                AdProvider = CreateRouteRecordingAdProvider(accessManager, internalRoutes, () => client, canShow: true,
                    isInternal: true),
                ProviderName = "internal"
            },
            new AppAdProviderItem {
                AdProvider = CreateRouteRecordingAdProvider(accessManager, paidRoutes, () => client, canShow: false),
                ProviderName = "paid"
            }
        ];

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(accessManager.GetToken(accessToken).ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);

        CollectionAssert.AreEqual(new AdRoute?[] { AdRoute.BeforeVpn, AdRoute.Passthrough }, paidRoutes);
        CollectionAssert.AreEqual(new AdRoute?[] { AdRoute.Passthrough }, internalRoutes);
    }

    // The extension was offered where no rewarded network serves the person's country, and failed there every time
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task The_extension_is_offered_only_where_a_rewarded_network_serves_the_person(bool isExcludedHere)
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        accessManager.CanExtendPremiumByAd = true;
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdProviderItems = [
            new AppAdProviderItem {
                AdProvider = new TestAdProvider(accessManager),
                ExcludeCountryCodes = isExcludedHere ? [AppRegionInfo.CurrentRegion.Name] : []
            }
        ];

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: TestHelper.CreateNullDevice());
        var token = await accessManager.CreateToken(TestCt);
        token.IsPublic = true;
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, TestCt, ConnectPlanId.PremiumByTrial);

        // the server offers it in both: the network alone decides
        Assert.AreEqual(!isExcludedHere, app.State.SessionStatus?.CanExtendByRewardedAd);
    }

    // An extension's ad whose window went during its try left the session waiting, its new traffic around the tunnel:
    // nothing shows an extension's ad later
    [TestMethod]
    public async Task A_window_gone_during_an_extensions_ad_ends_its_wait()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        accessManager.CanExtendPremiumByAd = true;

        var adProvider = new TestAdProvider(accessManager);
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdProviderItems = [new AppAdProviderItem { AdProvider = adProvider }];
        VpnHoodClient? client = null;
        var device = new TestDevice(TestAppHelper, _ => new TestVpnAdapter(TestAppHelper.CreateTestVpnAdapterOptions())) {
            ClientCreated = x => client = x
        };

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: device);
        var token = await accessManager.CreateToken(TestCt);
        token.IsPublic = true;
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, TestCt, ConnectPlanId.PremiumByTrial);

        // the person leaves the app while the ad loads
        adProvider.LoadAdCallback = () => throw new UiContextNotAvailableException();
        await Assert.ThrowsExactlyAsync<ShowAdNoUiException>(() => app.Api.App.ExtendByRewardedAd(TestCt));
        var session = (ClientSession)(client?.Session ?? throw new InvalidOperationException("No session."));
        Assert.IsFalse(session.AdHandler.IsWaitingForAd);
        Assert.IsFalse(session.PassthroughState.PassthroughForAd);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
    }

    // A reward the server refused before the VPN was followed by a second rewarded ad once the VPN was up
    [TestMethod]
    public async Task A_reward_the_server_refuses_asks_for_no_second_ad()
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);
        appDom.AccessManager.CanExtendPremiumByAd = true;
        appDom.AccessManager.RejectAllAds = true;

        var ex = await Assert.ThrowsExactlyAsync<SessionException>(() =>
            appDom.Connect(ConnectPlanId.NormalByRewardedAd));

        Assert.AreEqual(SessionErrorCode.RewardedAdRejected, ex.SessionResponse.ErrorCode);
        Assert.AreEqual(1, appDom.TestAdProvider.LoadAdCount);
    }

    // A rewarded connect's ad that failed before the VPN made the app disconnect, while the session went on to try it
    // once the VPN was up: the person never got the next try. The session alone decides what a failure means
    [TestMethod]
    public async Task A_rewarded_connects_ad_that_fails_before_the_VPN_is_tried_again_after_it()
    {
        await using var appDom = await AppClientServerDom.Create(TestAppHelper);
        appDom.AccessManager.CanExtendPremiumByAd = true;
        var adProvider = appDom.TestAdProvider;
        adProvider.LoadAdCallback = () => adProvider.LoadAdCount == 1
            ? throw new LoadAdException("The first load fails for the test.")
            : Task.CompletedTask;

        await appDom.Connect(ConnectPlanId.PremiumByRewardedAd);
        Assert.AreEqual(2, adProvider.LoadAdCount);
        Assert.IsNull(appDom.App.State.SessionStatus?.SessionExpirationTime, "The reward must be granted.");
    }

    // An ad blocker the app refuses, met by an ad during a session, left the session waiting for the ad, its new traffic
    // around the tunnel. No route helps it, so the wait ends at once
    [TestMethod]
    public async Task An_ad_blocker_met_during_a_session_ends_its_wait_for_the_ad()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        accessManager.CanExtendPremiumByAd = true;

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdProviderItems = [
            new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) { FailLoad = true } }
        ];
        appOptions.DeviceUiProvider = new TestDeviceUiProvider {
            SystemPrivateDns = new PrivateDns { Provider = "adblocker.test", IsActive = true }
        };

        VpnHoodClient? client = null;
        var device = new TestDevice(TestAppHelper, _ => new TestVpnAdapter(TestAppHelper.CreateTestVpnAdapterOptions())) {
            ClientCreated = x => client = x
        };

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: device);
        var token = await accessManager.CreateToken(TestCt);
        token.IsPublic = true;
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, TestCt, ConnectPlanId.PremiumByTrial);

        await Assert.ThrowsExactlyAsync<AdBlockerException>(() => app.Api.App.ExtendByRewardedAd(TestCt));
        var session = (ClientSession)(client?.Session ?? throw new InvalidOperationException("No session."));
        Assert.IsFalse(session.AdHandler.IsWaitingForAd);
        Assert.IsFalse(session.PassthroughState.PassthroughForAd);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
    }

    [TestMethod]
    public async Task The_ads_over_the_VPN_follow_the_networks_the_app_started_with()
    {
        // The builder's list loses its one network that can show over the VPN after the start: the loaders still have
        // it, from their own copies, so the session is still told an ad can go through the tunnel
        using var accessManager = await TestAppHelper.CreateAccessManager();
        var markedItem = new AppAdProviderItem {
            AdProvider = new TestAdProvider(accessManager, AdType.InterstitialAd),
            CanShowOverVpn = true
        };

        var adProviderItems = new List<AppAdProviderItem> {
            new() { AdProvider = new TestAdProvider(accessManager, AdType.InterstitialAd) },
            markedItem
        };

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdProviderItems = adProviderItems;
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        adProviderItems.Remove(markedItem);
        Assert.IsTrue(app.AdManager.AdService.CanShowInterstitialOverVpn);
    }

    // the internal ad, though it may show over the VPN, is no paid ad to try the tunnel for: its provider says so, with
    // no mark of the head's
    [TestMethod]
    public async Task An_internal_ad_alone_is_no_reason_to_try_the_tunnel()
    {
        using var accessManager = await TestAppHelper.CreateAccessManager();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdProviderItems = [
            new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager, AdType.InterstitialAd) },
            // a name of its own, as the app turns one named InternalAd off where its remote settings show none
            new AppAdProviderItem {
                AdProvider = new InternalInAdProvider(),
                ProviderName = "TestInternalAdProvider",
                CanShowOverVpn = true
            }
        ];

        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        Assert.IsFalse(app.AdManager.AdService.CanShowInterstitialOverVpn);
    }

    // A white-label app's owner names the networks that show over the VPN in the app's remote settings, in place of the
    // head's marks: one the head left out, or none, even where the head marked one
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task The_remote_settings_name_the_networks_that_show_over_the_VPN_in_place_of_the_heads_marks(
        bool isMarked, bool isNamed)
    {
        using var accessManager = await TestAppHelper.CreateAccessManager();
        var item = new AppAdProviderItem {
            AdProvider = new TestAdProvider(accessManager, AdType.InterstitialAd),
            ProviderName = "network",
            CanShowOverVpn = isMarked
        };

        // the copy the app downloaded at an earlier start; their address answers nothing, so no download replaces it
        var appOptions = TestAppHelper.CreateAppOptions();
        Directory.CreateDirectory(appOptions.StorageFolderPath);
        File.WriteAllText(Path.Combine(appOptions.StorageFolderPath, "remote_settings.json"),
            JsonSerializer.Serialize(new RemoteSettings { OverVpnAdProviders = isNamed ? ["network"] : [] }));

        appOptions.RemoteSettingsUrl = new Uri("http://127.0.0.1:9/remote_settings.json");
        appOptions.AdProviderItems = [item];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        Assert.AreEqual(isNamed, app.AdManager.AdService.CanShowInterstitialOverVpn);
        Assert.AreEqual(isNamed, item.CanShowOverVpn, "The loaders read the mark on the route through the tunnel.");
    }

    [TestMethod]
    public async Task Adblocker_exception()
    {
        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.DeviceUiProvider = new TestDeviceUiProvider {
            SystemPrivateDns = new PrivateDns {
                Provider = "adblocker.test", //this would be failed
                IsActive = true
            }
        };

        // simulate adblocker by making ad load to fail with adblocker exception and also blocking ad provider endpoint
        await using var appDom = await AppClientServerDom.Create(TestAppHelper, 
            adProviderAdType: AdType.InterstitialAd, appOptions: appOptions);

        var adProvider = appDom.TestAdProvider;
        var app = appDom.App;

        // simulate adblocker
        adProvider.LoadAdCompletionSource = new TaskCompletionSource();
        adProvider.LoadAdCallback = async () => {
            if (adProvider.LoadAdCount == 1) // fail first time to use after adapter load
                throw new LoadAdException("Test load failed.");

            // simulate adblocker
            using var tcpClient = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await tcpClient.ConnectAsync(new IPEndPoint(MockEps.HttpsV4EndPoint1.Address, 853), timeout.Token);
            throw new LoadAdException("Test load failed.");
        };

        // connect
        var token = await appDom.AccessManager.CreateToken(TestCt, adRequirement: AdRequirement.Flexible);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        _ = app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);

        // we don't use tls over dns detection, so the ad should fail after first attempt by checking the provider
        await AssertEqualsWait(1, () => adProvider.LoadAdCount);

        // wait for AdBlockerException
        await AssertEqualsWait(AppConnectionState.None, () => app.State.ConnectionState);
        Assert.AreEqual(nameof(AdBlockerException), app.State.LastError?.TypeName);
    }

    [TestMethod]
    public async Task Consent_form_does_not_count_toward_the_ad_load_limit()
    {
        // the person reads the form for longer than the app waits for an ad to load
        var consentProvider = new TestAdConsentProvider { ReadingTime = TimeSpan.FromSeconds(3) };
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.LoadAdTimeout = TimeSpan.FromSeconds(1);
        appOptions.AdOptions.ConsentProvider = consentProvider;
        await using var appDom = await AppClientServerDom.Create(TestAppHelper,
            adProviderAdType: AdType.InterstitialAd, appOptions: appOptions);

        var adProvider = appDom.TestAdProvider;
        var isLoadedAfterConsent = false;
        adProvider.LoadAdCallback = () => {
            isLoadedAfterConsent = consentProvider.IsGathered;
            return Task.CompletedTask;
        };

        // the ad loads once, after the answer
        var token = await appDom.AccessManager.CreateToken(TestCt, adRequirement: AdRequirement.Flexible);
        var vpnProfile = appDom.App.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await appDom.App.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        Assert.IsTrue(isLoadedAfterConsent);
        Assert.AreEqual(1, adProvider.LoadAdCount);
    }

    [TestMethod]
    public async Task Rewarded_ad_load_has_no_app_time_limit()
    {
        // the person asked to watch it, so a load slower than the app's limit still shows
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.LoadAdTimeout = TimeSpan.FromSeconds(1);
        await using var appDom = await AppClientServerDom.Create(TestAppHelper, appOptions: appOptions);
        appDom.TestAdProvider.LoadAdCallback = () => Task.Delay(TimeSpan.FromSeconds(3));

        await appDom.Connect(ConnectPlanId.PremiumByRewardedAd);
        Assert.AreEqual(1, appDom.TestAdProvider.LoadAdCount);
        Assert.IsNull(appDom.App.State.SessionStatus?.SessionExpirationTime, "The reward must be granted.");
    }

    [TestMethod]
    public async Task Flexible_ad_load_keeps_the_app_time_limit()
    {
        // a Free Server's ad: past the limit, the person connects without it
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.LoadAdTimeout = TimeSpan.FromSeconds(1);
        await using var appDom = await AppClientServerDom.Create(TestAppHelper,
            adProviderAdType: AdType.InterstitialAd, appOptions: appOptions);
        appDom.TestAdProvider.LoadAdCallback = () => Task.Delay(TimeSpan.FromSeconds(10));

        var token = await appDom.AccessManager.CreateToken(TestCt, adRequirement: AdRequirement.Flexible);
        var vpnProfile = appDom.App.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        await appDom.App.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        Assert.AreEqual(2, appDom.TestAdProvider.LoadAdCount, "Once before the tunnel is up and once after.");
    }

    // the internal provider waits for the last route with no mark of the head's, the failing network tried on each
    [TestMethod]
    public async Task An_unmarked_internal_ad_shows_on_the_last_route()
    {
        // create server
        using var accessManager = await TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create access item
        var accessToken = await accessManager.AccessTokenService.Create(TestCt, adRequirement: AdRequirement.Flexible);
        var token = accessManager.GetToken(accessToken);

        // configure client app for ad
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdOptions.LoadAdPostDelay = TimeSpan.Zero;
        var testAdProvider = new TestAdProvider(accessManager, AdType.InterstitialAd) {
            FailLoad = true
        };
        appOptions.AdProviderItems = [
            new AppAdProviderItem {
                AdProvider = testAdProvider
            },
            new AppAdProviderItem {
                AdProvider = new InternalInAdProvider(),
                ProviderName = "TestInternalAdProvider"
            }
        ];

        // create client app
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        // connect
        _ = app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt); // don't await as it will wait for ad to load
        await app.WaitForState(AppConnectionState.WaitingForAd, TestCt);
        await AssertEqualsWait(true, () => app.State.IsWaitingForInternalAd);
        await AssertEqualsWait(2, () => testAdProvider.LoadAdCount,
            "two times must be tried to reach the internal ad.");

        app.AdManager.AdService.InternalAdDismiss(ShowAdResult.Clicked);
        await app.WaitForState(AppConnectionState.Connected, TestCt);
    }

    // a network that records the route of each of its loads, which fail where it may not show
    private static TestAdProvider CreateRouteRecordingAdProvider(TestAccessManager accessManager,
        List<AdRoute?> routes, Func<VpnHoodClient?> getClient, bool canShow, bool isInternal = false)
    {
        var adProvider = new TestAdProvider(accessManager, AdType.InterstitialAd) { IsInternal = isInternal };
        adProvider.LoadAdCallback = () => {
            routes.Add(getClient()?.Session?.Status.AdRoute);
            return canShow ? Task.CompletedTask : throw new LoadAdException("The load fails for the test.");
        };

        return adProvider;
    }
}
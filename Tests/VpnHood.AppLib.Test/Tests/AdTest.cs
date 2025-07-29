using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class AdTest : TestAppBase
{
    [TestMethod]
    public async Task flexible_ad_should_not_close_session_if_load_ad_failed()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create access item
        var accessToken = accessManager.AccessTokenService.Create(adRequirement: AdRequirement.Flexible);

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProvider = new TestAdProvider(accessManager, AppAdType.InterstitialAd);
        var adProviderItem = new AppAdProviderItem { AdProvider = adProvider, ProviderName = "UnitTestAd" };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        adProvider.FailLoad = true;
        adProvider.FailShow = true; // should not reach this state

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessManager.GetToken(accessToken).ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);
    }

    [TestMethod]
    public async Task flexible_ad_should_close_session_if_display_ad_failed()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem {
            AdProvider = new TestAdProvider(accessManager, AppAdType.InterstitialAd),
            ProviderName = "UnitTestAd"
        };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        AppUiContext.Context = null;
        //adProviderItem.FailShow = true;

        // connect
        var token = accessManager.CreateToken(adRequirement: AdRequirement.Flexible);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await Assert.ThrowsExceptionAsync<ShowAdNoUiException>(() => app.Connect(clientProfile.ClientProfileId));
        await app.WaitForState(AppConnectionState.None);
    }

    [TestMethod]
    public async Task flexible_ad_should_not_be_displayed_on_trial()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        AppUiContext.Context = null;
        //adProviderItem.FailShow = true;

        // connect
        var token = accessManager.CreateToken(adRequirement: AdRequirement.Flexible);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, planId: ConnectPlanId.PremiumByTrial);
        await app.WaitForState(AppConnectionState.Connected);
    }

    [TestMethod]
    public async Task Session_must_be_closed_after_few_minutes_if_ad_is_not_accepted()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        AppUiContext.Context = null;
        //adProviderItem.FailShow = true;

        // connect
        var token = accessManager.CreateToken();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await Assert.ThrowsExceptionAsync<ShowAdNoUiException>(() => app.Connect(new ConnectOptions {
            ClientProfileId = clientProfile.ClientProfileId,
            PlanId = ConnectPlanId.PremiumByRewardedAd
        }));

        await app.WaitForState(AppConnectionState.None);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RewardedAd_expiration_must_be_increased_by_plan_id(bool acceptAd)
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        accessManager.CanExtendPremiumByAd = true;
        accessManager.RejectAllAds = !acceptAd;

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);

        // create access token
        var token = accessManager.CreateToken();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // connect
        if (acceptAd) {
            await app.Connect(clientProfile.ClientProfileId, ConnectPlanId.PremiumByRewardedAd);
            Assert.IsNull(app.State.SessionStatus?.SessionExpirationTime);
        }
        else {
            var ex = await Assert.ThrowsExceptionAsync<SessionException>(() =>
                app.Connect(clientProfile.ClientProfileId, ConnectPlanId.PremiumByRewardedAd));
            Assert.AreEqual(SessionErrorCode.RewardedAdRejected, ex.SessionResponse.ErrorCode);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RewardedAd_expiration_must_be_increased_by_user(bool acceptAd)
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
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
        var token = accessManager.CreateToken();
        token.IsPublic = true;

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, ConnectPlanId.PremiumByTrial);

        // assert
        Assert.IsNotNull(app.State.SessionStatus?.SessionExpirationTime);

        // show ad
        if (acceptAd) {
            await app.AdManager.ExtendByRewardedAd(CancellationToken.None);
            Assert.IsNull(app.State.SessionStatus?.SessionExpirationTime);
        }
        else {
            var ex = await Assert.ThrowsExceptionAsync<SessionException>(() =>
                app.AdManager.ExtendByRewardedAd(CancellationToken.None));
            Assert.AreEqual(SessionErrorCode.RewardedAdRejected, ex.SessionResponse.ErrorCode);
            await Task.Delay(500);
            await app.WaitForState(AppConnectionState.Connected);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RewardedAd_should_toggle_by_access_manager(bool enable)
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        accessManager.CanExtendPremiumByAd = enable;

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        var device = TestHelper.CreateNullDevice();
        await using var app = TestAppHelper.CreateClientApp(device: device, appOptions: appOptions);

        // create token
        var token = accessManager.CreateToken();
        token.IsPublic = true;

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, ConnectPlanId.PremiumByTrial);

        // asserts
        Assert.AreEqual(enable, app.State.SessionStatus?.CanExtendByRewardedAd);
    }

    [TestMethod]
    public async Task ShowAd_must_change_state_to_WaitingForAd()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create access item
        var accessToken = accessManager.AccessTokenService.Create(adRequirement: AdRequirement.Flexible);

        // configure client app for ad
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdOptions.LoadAdPostDelay = TimeSpan.FromSeconds(20);
        var adProvider = new TestAdProvider(accessManager, AppAdType.InterstitialAd);
        var adProviderItem = new AppAdProviderItem { AdProvider = adProvider };
        appOptions.AdProviderItems = [adProviderItem];

        // create client app
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessManager.GetToken(accessToken).ToAccessKey());
        var isAdLoadingStatusMet = false;
        app.ConnectionStateChanged += (_, _) => {
            // ReSharper disable once AccessToDisposedClosure
            if (app.ConnectionState == AppConnectionState.WaitingForAd)
                isAdLoadingStatusMet = true;
        };

        // connect
        _ = app.Connect(clientProfile.ClientProfileId); // don't await as it will wait for ad to load
        await app.WaitForState(AppConnectionState.WaitingForAd);
        await VhTestUtil.AssertEqualsWait(true, () => isAdLoadingStatusMet);
    }

    [TestMethod]
    public async Task RewardedAd_should_call_BindProcessToVpn()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        accessManager.CanExtendPremiumByAd = true;

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager), ProviderName = "TestAd"};
        appOptions.AdOptions.PreloadAd = false;
        appOptions.AdOptions.LoadAdPostDelay = TimeSpan.FromSeconds(1);
        appOptions.AdProviderItems = [adProviderItem];
        var device = TestHelper.CreateNullDevice();
        await using var app = TestAppHelper.CreateClientApp(device: device, appOptions: appOptions);

        // create token
        var token = accessManager.CreateToken();
        token.IsPublic = true;

        Assert.AreEqual(0, device.BindProcessToVpnFalseCount);
        Assert.AreEqual(0, device.BindProcessToVpnTrueCount);
        Assert.AreEqual(false, device.LastBindProcessToVpnValue);

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, ConnectPlanId.PremiumByRewardedAd);

        // asserts
        await Task.Delay(1000);
        Assert.AreEqual(1, device.BindProcessToVpnTrueCount);
        Assert.AreEqual(1, device.BindProcessToVpnFalseCount);
        Assert.AreEqual(true, device.LastBindProcessToVpnValue);
    }

}
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.Adapters;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Test.Device;

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
        var adProviderItem = new AppAdProviderItem { AdProvider = adProvider };
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
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager, AppAdType.InterstitialAd) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        ActiveUiContext.Context = null;
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
        ActiveUiContext.Context = null;
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
        ActiveUiContext.Context = null;
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
        var device = new TestDevice(TestHelper,() => new NullVpnAdapter { CanDetectInProcessPacket = true });
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
            await app.ExtendByRewardedAd(CancellationToken.None);
            Assert.IsNull(app.State.SessionStatus?.SessionExpirationTime);
        }
        else {
            var ex = await Assert.ThrowsExceptionAsync<SessionException>(() =>
                app.ExtendByRewardedAd(CancellationToken.None));
            Assert.AreEqual(SessionErrorCode.RewardedAdRejected, ex.SessionResponse.ErrorCode);
            await Task.Delay(500);
            await app.WaitForState(AppConnectionState.Connected);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RewardedAd_should_toggle_by_canDetectInProcessPacket(bool canDetectInProcessPacket)
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        accessManager.CanExtendPremiumByAd = true;

        // create client app
        var appOptions = TestAppHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        var device = new TestDevice(TestHelper, () => new NullVpnAdapter { CanDetectInProcessPacket = canDetectInProcessPacket });
        await using var app = TestAppHelper.CreateClientApp(device: device, appOptions: appOptions);

        // create token
        var token = accessManager.CreateToken();
        token.IsPublic = true;

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, ConnectPlanId.PremiumByTrial);

        // asserts
        Assert.AreEqual(canDetectInProcessPacket, app.State.SessionStatus?.CanExtendByRewardedAd);
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
        var device = new TestDevice(TestHelper, () => new NullVpnAdapter { CanDetectInProcessPacket = true });
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
}
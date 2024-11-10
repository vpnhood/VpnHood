using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client.App;
using VpnHood.Client.Device;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class AdTest : TestBase
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
        var appOptions = TestHelper.CreateAppOptions();
        var adProvider = new TestAdProvider(accessManager);
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);
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

        // create access item
        var accessToken = accessManager.AccessTokenService.Create(adRequirement: AdRequirement.Flexible);

        // create client app
        var appOptions = TestHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);
        ActiveUiContext.Context = null;
        //adProviderItem.FailShow = true;

        // connect
        var token = accessManager.GetToken(accessToken);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await Assert.ThrowsExceptionAsync<ShowAdNoUiException>(() => app.Connect(clientProfile.ClientProfileId));
        await TestHelper.WaitForAppState(app, AppConnectionState.None);
    }

    [TestMethod]
    public async Task Session_must_be_closed_after_few_minutes_if_ad_is_not_accepted()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create access item
        var accessToken = accessManager.AccessTokenService.Create(adRequirement: AdRequirement.Required);

        // create client app
        var appOptions = TestHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);
        ActiveUiContext.Context = null;
        //adProviderItem.FailShow = true;

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessManager.GetToken(accessToken).ToAccessKey());
        await Assert.ThrowsExceptionAsync<ShowAdNoUiException>(() => app.Connect(clientProfile.ClientProfileId));
        await TestHelper.WaitForAppState(app, AppConnectionState.None);
    }

    [TestMethod]
    public async Task Session_expiration_must_increase_by_ad()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create access item
        var accessToken = accessManager.AccessTokenService.Create(adRequirement: AdRequirement.Required);

        // create client app
        var appOptions = TestHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        // connect
        var token = accessManager.GetToken(accessToken);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);

        // assert
        await VhTestUtil.AssertEqualsWait(null, () => app.State.SessionStatus?.AccessUsage?.ExpirationTime);
    }

    [TestMethod]
    public async Task Session_exception_should_be_short_if_ad_is_not_accepted()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create access item
        var accessToken = accessManager.AccessTokenService.Create(adRequirement: AdRequirement.Required);
        accessManager.RejectAllAds = true; // server will reject all ads

        // create client app
        var appOptions = TestHelper.CreateAppOptions();
        var adProviderItem = new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager) };
        appOptions.AdProviderItems = [adProviderItem];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        // connect
        var token = accessManager.GetToken(accessToken);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);

        // asserts
        Assert.IsNotNull(app.State.SessionStatus?.AccessUsage?.ExpirationTime);
        Assert.IsTrue(app.State.SessionStatus.AccessUsage.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
    }
}
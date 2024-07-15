using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client.App;
using VpnHood.Client.App.Exceptions;
using VpnHood.Client.Device;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Test.Services;

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
        var accessItem = accessManager.AccessItem_Create(adRequirement: AdRequirement.Flexible);
        accessItem.Token.ToAccessKey();

        // create client app
        var appOptions = TestHelper.CreateAppOptions();
        var adService = new TestAdService(accessManager);
        appOptions.AdServices = [adService];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);
        adService.FailLoad = true;
        adService.FailShow = true; // should not reach this state

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessItem.Token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);
    }

    [TestMethod]
    public async Task flexible_ad_should_close_session_if_display_ad_failed()
    {
        // create server
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create access item
        var accessItem = accessManager.AccessItem_Create(adRequirement: AdRequirement.Flexible);
        accessItem.Token.ToAccessKey();

        // create client app
        var appOptions = TestHelper.CreateAppOptions();
        var adService = new TestAdService(accessManager);
        appOptions.AdServices = [adService];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);
        ActiveUiContext.Context = null;
        //adService.FailShow = true;

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessItem.Token.ToAccessKey());
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
        var accessItem = accessManager.AccessItem_Create(adRequirement: AdRequirement.Required);
        accessItem.Token.ToAccessKey();

        // create client app
        var appOptions = TestHelper.CreateAppOptions();
        var adService = new TestAdService(accessManager);
        appOptions.AdServices = [adService];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);
        ActiveUiContext.Context = null;
        //adService.FailShow = true;

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessItem.Token.ToAccessKey());
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
        var accessItem = accessManager.AccessItem_Create(adRequirement: AdRequirement.Required);
        accessItem.Token.ToAccessKey();

        // create client app
        var appOptions = TestHelper.CreateAppOptions();
        appOptions.AdServices = [new TestAdService(accessManager)];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessItem.Token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);

        // assert
        await VhTestUtil.AssertEqualsWait(null, () => app.State.SessionStatus?.AccessUsage?.ExpirationTime);
    }

    [TestMethod]
    public async Task Session_exception_should_be_short_if_ad_is_not_accepted()
    {
        // create server
        using var testManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(testManager);

        // create access item
        var accessItem = testManager.AccessItem_Create(adRequirement: AdRequirement.Required);
        accessItem.Token.ToAccessKey();
        testManager.RejectAllAds = true; // server will reject all ads

        // create client app
        var appOptions = TestHelper.CreateAppOptions();
        appOptions.AdServices = [new TestAdService(testManager)];
        await using var app = TestHelper.CreateClientApp(appOptions: appOptions);

        // connect
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessItem.Token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);

        // asserts
        Assert.IsNotNull(app.State.SessionStatus?.AccessUsage?.ExpirationTime);
        Assert.IsTrue(app.State.SessionStatus.AccessUsage.ExpirationTime < DateTime.UtcNow.AddMinutes(10));
    }
}
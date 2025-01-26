using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.Test;
using VpnHood.Test.Tests;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class AccessCodeTest : TestBase
{
    [TestMethod]
    public async Task AaFoo()
    {
        await Task.Delay(1);
    }

    [TestMethod]
    public async Task AccessCode_Accept()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);

        // create client app
        var token1 = TestHelper.CreateAccessToken(server);
        var token2 = TestHelper.CreateAccessToken(server, maxClientCount: 6);

        // create access code and add it to test manager
        var accessCode = TestAppHelper.BuildAccessCode();
        accessManager.AccessCodes.Add(accessCode, token2.TokenId);

        // create access code
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        app.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams {
            AccessCode = accessCode
        });

        // connect
        await app.Connect(clientProfile.ClientProfileId);
        Assert.AreEqual(6, app.State.SessionInfo?.AccessInfo?.MaxDeviceCount, "token2 must be used instead of token1 due the access code.");
    }
}
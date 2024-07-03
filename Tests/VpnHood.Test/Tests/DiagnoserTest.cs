using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client.Exceptions;

namespace VpnHood.Test.Tests;

[TestClass]
public class DiagnoserTest : TestBase
{
    [TestMethod]
    public async Task NormalConnect_NoInternet()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        token.ServerToken.HostEndPoints = [TestConstants.InvalidEp];

        // create client
        var appOptions = TestHelper.CreateAppOptions();
        appOptions.AutoDiagnose = true;
        await using var clientApp = TestHelper.CreateClientApp(appOptions: appOptions);
        var clientProfile = clientApp.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // NoInternetException
        clientApp.Diagnoser.TestHttpUris = [TestConstants.InvalidUri];
        clientApp.Diagnoser.TestNsIpEndPoints = [TestConstants.InvalidEp];
        clientApp.Diagnoser.TestPingIpAddresses = [TestConstants.InvalidIp];

        try
        {
            await clientApp.Connect(clientProfile.ClientProfileId);
        }
        catch (Exception ex) 
        {
            Assert.AreEqual(nameof(NoInternetException), ex.GetType().Name);
        }
    }
}
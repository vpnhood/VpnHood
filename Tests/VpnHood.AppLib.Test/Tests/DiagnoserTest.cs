using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Test;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class DiagnoserTest : TestAppBase
{
    [TestMethod]
    public async Task NormalConnect_NoInternet()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        token.ServerToken.HostEndPoints = [TestConstants.InvalidEp];

        // create client
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AutoDiagnose = true;
        appOptions.ConnectTimeout = TimeSpan.FromSeconds(30);
        await using var clientApp = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var clientProfile = clientApp.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // NoInternetException
        clientApp.Diagnoser.TestHttpUris = [TestConstants.InvalidUri];
        clientApp.Diagnoser.TestNsIpEndPoints = [TestConstants.InvalidEp];
        clientApp.Diagnoser.TestPingIpAddresses = [TestConstants.InvalidIp];
        await Assert.ThrowsExactlyAsync<NoInternetException>(() =>
            clientApp.Connect(clientProfile.ClientProfileId));
    }
}
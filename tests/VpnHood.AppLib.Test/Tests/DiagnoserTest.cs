using VpnHood.AppLib.Exceptions;
using VpnHood.AppLib.Test.Dom;
using VpnHood.Core.Client.Abstractions.Exceptions;

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
        token.ServerToken.HostEndPoints = [MockEps.HttpV4EndPointInvalid];

        // create client
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.AutoDiagnose = true;
        appOptions.ConnectTimeout = TimeSpan.FromSeconds(30);
        await using var clientApp = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var clientProfile = clientApp.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // NoInternetException
        clientApp.Diagnoser.TestHttpUris = [MockEps.HttpUrlInvalid];
        clientApp.Diagnoser.TestNsIpEndPoints = [MockEps.HttpV4EndPointInvalid];
        clientApp.Diagnoser.TestPingIpAddresses = [MockEps.IpInvalid];
        await Assert.ThrowsExactlyAsync<NoInternetException>(() =>
            clientApp.Connect(clientProfile.ClientProfileId));

        Assert.AreEqual(nameof(NoInternetException), clientApp.State.LastError?.TypeName);
    }

    [TestMethod]
    public async Task UnreachableServer()
    {
        await using var dom = await AppClientServerDom.CreateWithNullCapture(TestAppHelper);

        // change access key endpoint
        var token = dom.ClientProfile.Token;
        token.ServerToken.HostEndPoints = [MockEps.HttpV4EndPointInvalid];
        var clientProfile = dom.App.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // NoInternetException
        await Assert.ThrowsExactlyAsync<UnreachableServerException>(() =>
            dom.App.Connect(clientProfile.ClientProfileId, diagnose: true));

        Assert.AreEqual(nameof(UnreachableServerException), dom.App.State.LastError?.TypeName);
    }
}
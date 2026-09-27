using VpnHood.AppLib.Api.Exceptions;
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
        var vpnProfile = clientApp.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // NoInternetException
        clientApp.Diagnoser.TestHttpUris = [MockEps.HttpUrlInvalid];
        clientApp.Diagnoser.TestNsIpEndPoints = [MockEps.HttpV4EndPointInvalid];
        clientApp.Diagnoser.TestPingIpAddresses = [MockEps.IpInvalid];
        await Assert.ThrowsExactlyAsync<NoInternetException>(() =>
            clientApp.Connect(vpnProfile.VpnProfileId));

        Assert.AreEqual(nameof(NoInternetException), clientApp.State.LastError?.TypeName);
    }

    [TestMethod]
    public async Task UnreachableServer()
    {
        await using var dom = await AppClientServerDom.CreateWithNullCapture(TestAppHelper);

        // change access key endpoint
        var token = dom.VpnProfile.Token;
        token.ServerToken.HostEndPoints = [MockEps.HttpV4EndPointInvalid];
        var vpnProfile = dom.App.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // NoInternetException
        await Assert.ThrowsExactlyAsync<UnreachableServerException>(() =>
            dom.App.Connect(vpnProfile.VpnProfileId, diagnose: true));

        Assert.AreEqual(nameof(UnreachableServerException), dom.App.State.LastError?.TypeName);
    }
}
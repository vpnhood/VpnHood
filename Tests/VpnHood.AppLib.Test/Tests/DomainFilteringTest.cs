using VpnHood.Test;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class DomainFilteringTest : TestAppBase
{
    [TestMethod]
    public async Task IncludeDomains()
    {
        // first create device to access its socket factory
        var vpnAdapterOptions = TestHelper.CreateTestVpnAdapterOptions();
        var device = TestHelper.CreateDevice(vpnAdapterOptions);

        // Create Server
        await using var server = await TestHelper.CreateServer(socketFactory: device.SocketFactory);

        // create app
        await using var app = TestAppHelper.CreateClientApp(device: device);
        app.UserSettings.DomainFilterPolicy.Excludes = [TestConstants.HttpsExternalUri1.Host];

        // connect
        var token = TestHelper.CreateAccessToken(server);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);
        await app.WaitForState(AppConnectionState.Connected);

        // text include
        var oldStat = app.GetSessionStatus();
        await TestHelper.Test_Https(uri: TestConstants.HttpsExternalUri1);
        var newStat = app.GetSessionStatus();
        Assert.AreEqual(oldStat.TcpTunnelledCount, newStat.TcpTunnelledCount);
        Assert.AreEqual(oldStat.TcpPassthruCount + 1, newStat.TcpPassthruCount);

        // text exclude
        oldStat = app.GetSessionStatus();
        await TestHelper.Test_Https(uri: TestConstants.HttpsExternalUri2);
        newStat = app.GetSessionStatus();
        Assert.AreEqual(oldStat.TcpTunnelledCount + 1, newStat.TcpTunnelledCount);
        Assert.AreEqual(oldStat.TcpPassthruCount, newStat.TcpPassthruCount);
    }

    [TestMethod]
    public async Task ExcludeDomains()
    {
        // first create device to access its socket factory
        var vpnAdapterOptions = TestHelper.CreateTestVpnAdapterOptions();
        var device = TestHelper.CreateDevice(vpnAdapterOptions);

        // Create Server
        await using var server = await TestHelper.CreateServer(socketFactory: device.SocketFactory);

        // create app
        await using var app = TestAppHelper.CreateClientApp(device: device);
        app.UserSettings.DomainFilterPolicy.Excludes = [TestConstants.HttpsExternalUri1.Host];

        // connect
        var token = TestHelper.CreateAccessToken(server);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);
        await app.WaitForState(AppConnectionState.Connected);

        // text include
        var oldStat = app.GetSessionStatus(TestCt);
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri2);
        var newStat = app.GetSessionStatus(TestCt);
        Assert.AreEqual(oldStat.TcpTunnelledCount + 1, newStat.TcpTunnelledCount);
        Assert.AreEqual(oldStat.TcpPassthruCount, newStat.TcpPassthruCount);

        // text exclude
        oldStat = app.GetSessionStatus();
        await TestHelper.Test_Https(uri: TestConstants.HttpsExternalUri1);
        newStat = app.GetSessionStatus();
        Assert.AreEqual(oldStat.TcpTunnelledCount, newStat.TcpTunnelledCount);
        Assert.AreEqual(oldStat.TcpPassthruCount + 1, newStat.TcpPassthruCount);
    }
}

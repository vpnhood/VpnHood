using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Toolkit.Utils;
using System.Net;

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientTunnelTest : TestBase
{
    [TestMethod]
    public async Task TcpChannel()
    {
        var clientVpnAdapter = TestHelper.CreateTestVpnAdapter();
        var testSocketFactory = TestHelper.CreateTestSocketFactory(clientVpnAdapter);

        // Create Server
        var serverEp = VhUtils.GetFreeTcpEndPoint(IPAddress.IPv6Loopback);
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.TcpEndPoints = [serverEp];
        fileAccessManagerOptions.PublicEndPoints = [serverEp];

        using var accessManager = TestHelper.CreateAccessManager(fileAccessManagerOptions);
        await using var server = await TestHelper.CreateServer(accessManager, socketFactory: testSocketFactory);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.UseUdpChannel = false;
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions, vpnAdapter: clientVpnAdapter);
        var clientServer = new ClientServer(server, client);

        await AssertTunnel(clientServer);

        // check HostEndPoint in server
        accessManager.SessionService.Sessions.TryGetValue(client.SessionId, out var session);
        Assert.IsTrue(token.ServerToken.HostEndPoints?.Any(x => x.Equals(session?.HostEndPoint)));

        // check UserAgent in server
        Assert.AreEqual(client.UserAgent, session?.ClientInfo.UserAgent);

        // check ClientPublicAddress in server
        Assert.AreEqual(serverEp.Address, client.SessionInfo?.ClientPublicIpAddress);
    }

    [TestMethod]
    public async Task UdpChannel()
    {
        VhLogger.IsDiagnoseMode = true;

        var clientVpnAdapter = TestHelper.CreateTestVpnAdapter();
        var testSocketFactory = TestHelper.CreateTestSocketFactory(clientVpnAdapter);

        // Create Server
        await using var server = await TestHelper.CreateServer(socketFactory: testSocketFactory);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        var clientOptions = TestHelper.CreateClientOptions(token, useUdpChannel: true);
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions, vpnAdapter: clientVpnAdapter);
        var clientServer = new ClientServer(server, client);

        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Testing by UdpChannel.");
        Assert.IsTrue(client.UseUdpChannel);
        await AssertTunnel(clientServer);

        // switch to tcp
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Switch to DatagramChannel.");
        client.UseUdpChannel = false;
        await AssertTunnel(clientServer);
        await VhTestUtil.AssertEqualsWait(false, () => client.GetSessionStatus().IsUdpMode);
        Assert.IsFalse(client.UseUdpChannel);

        // switch back to udp
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Switch back to UdpChannel.");
        client.UseUdpChannel = true;
        await AssertTunnel(clientServer);
        await VhTestUtil.AssertEqualsWait(true, () => client.GetSessionStatus().IsUdpMode);
        Assert.IsTrue(client.UseUdpChannel);
    }
    
    private static async Task AssertInvalidTcpRequest(ClientServer clientServer)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Invalid Https request.");
        using var httpClient = new HttpClient();
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            httpClient.GetStringAsync(TestConstants.HttpsRefusedUri));

        Assert.AreEqual(HttpRequestError.SecureConnectionError, ex.HttpRequestError);
        Assert.AreEqual(ClientState.Connected, clientServer.Client.State);

    }

    private async Task AssertValidTcp(ClientServer clientServer)
    {
        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Https");
        await TestHelper.Test_Https();
        
        clientServer.AssertTransfer(minTunnelReceivedData: 2000);

    }

    private async Task AssertValidUdp(ClientServer clientServer)
    {
        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Udp");
        await TestHelper.Test_Udp();
        
        clientServer.AssertTransfer();
    }

    private async Task AssertValidPingV4(ClientServer clientServer)
    {
        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Ping IPv4");
        await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1);

        clientServer.AssertTransfer();
    }

    private async Task AssertValidPingV6(ClientServer clientServer)
    {
        if (!await TestHelper.IsIpV6Supported())
            return; 

        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Ping IPv6");
        await TestHelper.Test_Ping(ipAddress: TestConstants.PingV6Address1);

        clientServer.AssertTransfer();
    }

    private async Task AssertTunnel(ClientServer clientServer)
    {
        await AssertInvalidTcpRequest(clientServer);
        await AssertValidTcp(clientServer);
        await AssertValidUdp(clientServer);
        await AssertValidPingV4(clientServer);
        await AssertValidPingV6(clientServer);
    }
}
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientTunnelTest : TestBase
{
    [TestMethod]
    public async Task TcpChannel()
    {
        await using var clientServerDom =  await ClientServerDom.Create(TestHelper);
        await AssertTunnel(clientServerDom);
    }

    [TestMethod]
    public async Task UdpChannel()
    {
        VhLogger.IsDiagnoseMode = true;
        VhLogger.Instance = VhLogger.CreateConsoleLogger(LogLevel.Trace); 
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, useUdpChannel: true);

        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Testing by UdpChannel.");
        Assert.IsTrue(clientServerDom.Client.UseUdpChannel);
        await AssertTunnel(clientServerDom);
    }


    [TestMethod]
    public async Task UdpChannel_Switch()
    {
        VhLogger.IsDiagnoseMode = true;
        VhLogger.Instance = VhLogger.CreateConsoleLogger(LogLevel.Trace); 
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, useUdpChannel: true);

        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Testing by UdpChannel.");
        Assert.IsTrue(clientServerDom.Client.UseUdpChannel);
        await AssertTunnel(clientServerDom);

        // switch to tcp
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Switch to PacketChannel.");
        clientServerDom.Client.UseUdpChannel = false;
        await VhTestUtil.AssertEqualsWait(true, () => clientServerDom.Client.SessionStatus?.PacketChannelCount > 0);
        await AssertTunnel(clientServerDom);
        await VhTestUtil.AssertEqualsWait(false, () => clientServerDom.Client.GetSessionStatus().IsUdpMode);
        Assert.IsFalse(clientServerDom.Client.UseUdpChannel);

        // switch back to udp
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Switch back to UdpChannel.");
        clientServerDom.Client.UseUdpChannel = true;
        await VhTestUtil.AssertEqualsWait(true, () => clientServerDom.Client.SessionStatus?.PacketChannelCount > 0);
        await AssertTunnel(clientServerDom);
        await VhTestUtil.AssertEqualsWait(true, () => clientServerDom.Client.GetSessionStatus().IsUdpMode);
        Assert.IsTrue(clientServerDom.Client.UseUdpChannel);
    }

    private static async Task AssertInvalidTcpRequest(ClientServerDom clientServer)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Invalid Https request.");
        using var httpClient = new HttpClient();

        // HttpsBlockedUri is faster than HttpsRefusedUri. 
        // In windows HttpsRefusedUri takes 2 seconds to return error
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => 
            httpClient.GetStringAsync(TestConstants.HttpsBlockedUri)); 

        Assert.AreEqual(HttpRequestError.SecureConnectionError, ex.HttpRequestError);
        Assert.AreEqual(ClientState.Connected, clientServer.Client.State);

    }

    private async Task AssertValidTcp(ClientServerDom clientServer)
    {
        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Https");
        await TestHelper.Test_Https();
        
        clientServer.AssertTransfer(minTunnelReceivedData: 2000);

    }

    private async Task AssertValidUdp(ClientServerDom clientServer)
    {
        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Udp");
        await TestHelper.Test_Udp();
        
        clientServer.AssertTransfer();
    }

    private async Task AssertValidPingV4(ClientServerDom clientServer)
    {
        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Ping IPv4");
        await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1);

        clientServer.AssertTransfer();
    }

    private async Task AssertValidPingV6(ClientServerDom clientServer)
    {
        if (!await TestHelper.IsIpV6Supported())
            return; 

        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Ping IPv6");
        await TestHelper.Test_Ping(ipAddress: TestConstants.PingV6Address1);

        clientServer.AssertTransfer();
    }

    private async Task AssertTunnel(ClientServerDom clientServerDom)
    {
        await AssertInvalidTcpRequest(clientServerDom);
        await AssertValidTcp(clientServerDom);
        await AssertValidUdp(clientServerDom);
        await AssertValidPingV4(clientServerDom);
        await AssertValidPingV6(clientServerDom);
    }
}
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Test.Dom;

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientTunnelTest : TestBase
{
    [TestMethod]
    public async Task TcpChannel()
    {
        await using var clientServerDom = await ClientServerDom.Create(TestHelper);
        await AssertTunnel(clientServerDom);
    }

    [TestMethod]
    public async Task UdpChannel()
    {
        VhLogger.MinLogLevel = LogLevel.Trace;
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Udp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOption);

        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Testing by UdpChannel.");
        Assert.AreEqual(ChannelProtocol.Udp, clientServerDom.Client.ChannelProtocol);
        await AssertTunnel(clientServerDom);
    }


    [TestMethod]
    public async Task UdpChannel_Switch()
    {
        VhLogger.MinLogLevel = LogLevel.Trace;
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Udp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOption);

        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Testing by UdpChannel.");
        Assert.AreEqual(ChannelProtocol.Udp, clientServerDom.Client.ChannelProtocol);
        await AssertTunnel(clientServerDom);

        // switch to tcp
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Switch to PacketChannel.");
        clientServerDom.Client.ChannelProtocol = ChannelProtocol.Tcp;
        await VhTestUtil.AssertEqualsWait(true,
            () => clientServerDom.Client.SessionStatus?.ActivePacketChannelCount > 0);
        await AssertTunnel(clientServerDom);
        await VhTestUtil.AssertEqualsWait(ChannelProtocol.Tcp,
            () => clientServerDom.Client.GetSessionStatus().ChannelProtocol);
        Assert.AreEqual(ChannelProtocol.Tcp, clientServerDom.Client.ChannelProtocol);
        Assert.IsTrue(clientServerDom.Client.SessionStatus?.IsTcpProxy);

        // switch back to udp
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Switch back to UdpChannel.");
        clientServerDom.Client.ChannelProtocol = ChannelProtocol.Udp;
        await VhTestUtil.AssertEqualsWait(true,
            () => clientServerDom.Client.SessionStatus?.ActivePacketChannelCount > 0);
        await AssertTunnel(clientServerDom);
        await VhTestUtil.AssertEqualsWait(ChannelProtocol.Udp,
            () => clientServerDom.Client.GetSessionStatus().ChannelProtocol);
        Assert.AreEqual(ChannelProtocol.Udp, clientServerDom.Client.ChannelProtocol);
        Assert.IsTrue(clientServerDom.Client.SessionStatus?.IsTcpProxy);
    }

    [TestMethod]
    public async Task TcpChannel_MultiChannel()
    {
        var clientOption = TestHelper.CreateClientOptions();
        clientOption.MinPacketChannelTimespan = TimeSpan.FromMilliseconds(500);
        clientOption.MaxPacketChannelTimespan = TimeSpan.FromMilliseconds(500);
        clientOption.MaxPacketChannelCount = 1;
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOption);
        await VhTestUtil.AssertEqualsWait(true, async () => {
            await VhUtils.TryInvokeAsync(null,
                () => TestHelper.Test_Udp(TimeSpan.FromMilliseconds(500))); // just try transfer
            return clientServerDom.Client.SessionStatus?.SessionPacketChannelCount >= 3;
        }, timeout: 6000);
    }


    private async Task AssertInvalidTcpRequest(ClientServerDom clientServer)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Invalid Https request.");
        using var httpClient = new HttpClient();

        // HttpsBlockedUri is faster than HttpsRefusedUri. 
        // In windows HttpsRefusedUri takes 2 seconds to return error
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            httpClient.GetStringAsync(TestConstants.HttpsBlockedUri));

        Assert.AreEqual(HttpRequestError.SecureConnectionError, ex.HttpRequestError);
        Assert.AreEqual(ClientState.Unstable, clientServer.Client.State);

        // bring back to normal state
        await TestHelper.Test_Https();
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
        await AssertValidTcp(clientServerDom);
        await AssertValidUdp(clientServerDom);
        await AssertValidPingV4(clientServerDom);
        await AssertValidPingV6(clientServerDom);
        await AssertInvalidTcpRequest(clientServerDom);
    }
}
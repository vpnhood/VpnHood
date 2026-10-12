using System.Net.Quic;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Test.Dom;
using VpnHood.Test.Providers;
// ReSharper disable ShortLivedHttpClient

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
        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Udp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOption);

        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Testing by UdpChannel.");
        Assert.AreEqual(ChannelProtocol.Udp, clientServerDom.Client.ChannelProtocol);
        await AssertTunnel(clientServerDom);
    }

    [TestMethod]
    public async Task QuicChannel()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            Assert.Inconclusive("QUIC is not supported on this platform.");

        VhLogger.MinLogLevel = LogLevel.Trace;
        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Quic);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOption);

        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Test: Testing by QuicChannel.");
        Assert.AreEqual(ChannelProtocol.Quic, clientServerDom.Client.ChannelProtocol);
        await AssertTunnel(clientServerDom);
    }


    [TestMethod]
    public async Task UdpChannel_Switch()
    {
        VhLogger.MinLogLevel = LogLevel.Trace;
        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Udp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOption);

        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Testing by UdpChannel.");
        Assert.AreEqual(ChannelProtocol.Udp, clientServerDom.Client.ChannelProtocol);
        await AssertTunnel(clientServerDom);

        // switch to tcp
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Switch to TCP PacketChannel.");
        clientServerDom.Client.ChannelProtocol = ChannelProtocol.Tcp;
        await VhTestUtil.AssertEqualsWait(true,
            () => clientServerDom.Client.RequiredSession.Status.ActivePacketChannelCount > 0,
            cancellationToken: TestCt);

        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Checking Tunnel using TCP PacketChannels.");
        await AssertTunnel(clientServerDom);
        await VhTestUtil.AssertEqualsWait(ChannelProtocol.Tcp,
            () => clientServerDom.Client.RequiredSession.Status.ChannelProtocol,
            cancellationToken: TestCt);

        Assert.AreEqual(ChannelProtocol.Tcp, clientServerDom.Client.ChannelProtocol);
        Assert.IsTrue(clientServerDom.Client.RequiredSession.Status.IsTcpProxy);

        // switch back to udp
        VhLogger.Instance.LogDebug(GeneralEventId.Test, "Switch back to UdpChannel.");
        clientServerDom.Client.ChannelProtocol = ChannelProtocol.Udp;
        await VhTestUtil.AssertEqualsWait(true,
            () => clientServerDom.Client.RequiredSession.Status.ActivePacketChannelCount > 0,
            cancellationToken: TestCt);

        await AssertTunnel(clientServerDom);
        await VhTestUtil.AssertEqualsWait(ChannelProtocol.Udp,
            () => clientServerDom.Client.RequiredSession.Status.ChannelProtocol,
            cancellationToken: TestCt);

        Assert.AreEqual(ChannelProtocol.Udp, clientServerDom.Client.ChannelProtocol);
        Assert.IsTrue(clientServerDom.Client.RequiredSession.Status.IsTcpProxy);
    }

    [TestMethod]
    public async Task TcpChannel_MultiChannel()
    {
        var clientOption = TestHelper.CreateClientOptions();
        clientOption.MinPacketChannelTimespan = TimeSpan.FromMilliseconds(500);
        clientOption.MaxPacketChannelTimespan = TimeSpan.FromMilliseconds(500);
        clientOption.Transport.MaxPacketChannelCount = 1;
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOption);
        await VhTestUtil.AssertEqualsWait(true, async () => {
            await VhUtils.TryInvokeAsync(null,
                () => TestHelper.Test_UdpEcho(timeout: TimeSpan.FromMilliseconds(500))); // just try transfer
            return clientServerDom.Client.RequiredSession.Status.SessionPacketChannelCount >= 3;
        }, timeout: 6000);
    }


    [TestMethod]
    [DataRow(ChannelProtocol.Tcp)]
    [DataRow(ChannelProtocol.Udp)]
    [DataRow(ChannelProtocol.Quic)]
    public async Task A_network_change_replaces_the_tunnels_connections(ChannelProtocol channelProtocol)
    {
        if (channelProtocol == ChannelProtocol.Quic && (!QuicListener.IsSupported || !QuicConnection.IsSupported))
            Assert.Inconclusive("QUIC is not supported on this platform.");

        var clientOption = TestHelper.CreateClientOptions(channelProtocol: channelProtocol);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper, clientOption);
        await AssertValidUdp(clientServerDom);
        await AssertValidTcp(clientServerDom);
        var createdChannelCount = clientServerDom.Client.RequiredSession.Status.SessionPacketChannelCount;

        clientServerDom.ClientVpnAdapter.SimulatePrimaryNetworkChange();

        // a packet sent before the new channel is up is lost, as on a real network change
        await VhTestUtil.AssertEqualsWait(true,
            () => clientServerDom.Client.RequiredSession.Status.ActivePacketChannelCount > 0,
            cancellationToken: TestCt);

        // both kinds of traffic again, on connections made after the change: TCP by the proxy channels
        await AssertValidUdp(clientServerDom);
        await AssertValidTcp(clientServerDom);
        if (channelProtocol == ChannelProtocol.Tcp)
            Assert.IsGreaterThan(createdChannelCount, clientServerDom.Client.RequiredSession.Status.SessionPacketChannelCount);
        Assert.AreEqual(ClientState.Connected, clientServerDom.Client.State);
    }

    [TestMethod]
    public async Task A_network_change_cuts_off_a_channel_still_connecting_on_the_old_network()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            Assert.Inconclusive("QUIC is not supported on this platform.");

        // a connect timeout the test would never wait out
        var socketFactory = new QuicConnectHoldingSocketFactory();
        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Quic);
        clientOption.Transport.TcpConnectTimeout = TimeSpan.FromSeconds(60);
        await using var clientServerDom =
            await ClientServerDom.Create(TestHelper, clientOption, clientSocketFactory: socketFactory);
        await AssertValidUdp(clientServerDom);

        // the first change's channel cannot connect: the host has gone on to another network
        socketFactory.HoldConnects = true;
        clientServerDom.ClientVpnAdapter.SimulatePrimaryNetworkChange();
        await VhTestUtil.AssertEqualsWait(true, () => socketFactory.HeldConnectCount > 0, cancellationToken: TestCt);

        // that network's change cuts the connect off, and its own channel comes up at once
        socketFactory.HoldConnects = false;
        clientServerDom.ClientVpnAdapter.SimulatePrimaryNetworkChange();
        await VhTestUtil.AssertEqualsWait(true,
            () => clientServerDom.Client.RequiredSession.Status.ActivePacketChannelCount > 0,
            cancellationToken: TestCt);
        await AssertValidUdp(clientServerDom);
        Assert.AreEqual(ClientState.Connected, clientServerDom.Client.State);
    }

    private async Task AssertInvalidTcpRequest(ClientServerDom clientServer)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Invalid Https request.");
        using var httpClient = new HttpClient();

        // HttpsBlockedUri is faster than HttpsRefusedUri. 
        // In windows HttpsRefusedUri takes 2 seconds to return error
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            httpClient.GetStringAsync(MockEps.HttpBlockedServerUri));

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

        await clientServer.AssertTransfer(minTunnelReceivedData: 2000);
    }

    private async Task AssertValidUdp(ClientServerDom clientServer)
    {
        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Udp");
        await TestHelper.Test_UdpEcho();

        await clientServer.AssertTransfer();
    }

    private async Task AssertValidPingV4(ClientServerDom clientServer)
    {
        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Ping IPv4");
        await TestHelper.Test_Ping(ipAddress: MockEps.PingV4Address1);

        await clientServer.AssertTransfer();
    }

    private async Task AssertValidPingV6(ClientServerDom clientServer)
    {
        if (!await TestHelper.IsIpV6Supported())
            return;

        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: Ping IPv6");
        await TestHelper.Test_Ping(ipAddress: MockEps.PingV6Address1);

        await clientServer.AssertTransfer();
    }

    private async Task AssertValidTcpData(ClientServerDom clientServer)
    {
        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: TcpUpload");
        await TestHelper.Test_TcpUpload();

        await clientServer.AssertClientSent();
        await clientServer.AssertServerReceived();

        clientServer.Collect();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test: TcpDownload");
        await TestHelper.Test_TcpDownload();

        await clientServer.AssertClientReceived();
        await clientServer.AssertServerSent();
    }

    private async Task AssertTunnel(ClientServerDom clientServerDom)
    {
        await AssertValidTcp(clientServerDom);
        await AssertValidUdp(clientServerDom);
        await AssertValidPingV4(clientServerDom);
        await AssertValidPingV6(clientServerDom);
        await AssertValidTcpData(clientServerDom);
        await AssertInvalidTcpRequest(clientServerDom);
    }
}
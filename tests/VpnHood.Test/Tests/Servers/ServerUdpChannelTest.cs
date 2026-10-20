using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Dom;
using VpnHood.Test.Extensions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests.Servers;

// a forged datagram, or a copy of an older one, must change no session state on the server; a move off a dead listener
// must still work
[TestClass]
public class ServerUdpChannelTest : TestBase
{
    [TestMethod]
    public async Task Forged_datagram_keeps_a_tcp_session_on_tcp()
    {
        await using var dom = await ClientServerDom.Create(TestHelper,
            TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Tcp));
        await TestHelper.Test_UdpEcho();

        var clientSession = (ClientSession)dom.Client.RequiredSession;
        var hostUdpEndPoint = clientSession.Config.HostUdpEndPoint;
        Assert.IsNotNull(hostUdpEndPoint, "The server must listen on UDP.");
        var createdChannelCount = clientSession.CreatedPacketChannelCount;
        var tcpChannelCount = dom.ServerSession.TcpChannelCount;

        // the forged datagram, given time to reach the read loop
        using var forger = new UdpClient(hostUdpEndPoint.AddressFamily);
        await forger.SendAsync(UdpTestUtil.CreateForgedDatagram(dom.ServerSession.SessionId), hostUdpEndPoint, TestCt);
        await Task.Delay(500, TestCt);

        // "the tunnel keeps working" alone would pass on a torn-down session too, once the client rebuilt its channel
        Assert.IsFalse(dom.ServerSession.UseUdpChannel);
        Assert.AreEqual(tcpChannelCount, dom.ServerSession.TcpChannelCount);
        await UdpTestUtil.AssertNothingReceived(forger, TestCt);
        await TestHelper.Test_UdpEcho();
        Assert.AreEqual(createdChannelCount, clientSession.CreatedPacketChannelCount,
            "The client must still use its first TCP packet channel.");
    }

    // the forged datagram made the session's transport; the client's first UDP datagram is its newest, and opens UDP
    [TestMethod]
    public async Task Udp_works_after_a_forged_datagram()
    {
        await using var dom = await ClientServerDom.Create(TestHelper,
            TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Tcp));
        await TestHelper.Test_UdpEcho();

        var hostUdpEndPoint = dom.Client.RequiredSession.Config.HostUdpEndPoint;
        Assert.IsNotNull(hostUdpEndPoint, "The server must listen on UDP.");
        using var forger = new UdpClient(hostUdpEndPoint.AddressFamily);
        await forger.SendAsync(UdpTestUtil.CreateForgedDatagram(dom.ServerSession.SessionId), hostUdpEndPoint, TestCt);
        await Task.Delay(500, TestCt);
        Assert.IsFalse(dom.ServerSession.UseUdpChannel);

        // a packet sent while the client is still swapping its channels is dropped: wait for the UDP one first
        dom.Client.ChannelProtocol = ChannelProtocol.Udp;
        await VhTestUtil.AssertEqualsWait(true,
            () => dom.Client.RequiredSession.Status.ActivePacketChannelCount > 0, cancellationToken: TestCt);
        await TestHelper.Test_UdpEcho();
        Assert.IsTrue(dom.ServerSession.UseUdpChannel);
    }

    // the listener dies and comes back on the same endpoint: the client's next datagram moves the session's transport
    // to the new one
    [TestMethod]
    public async Task Udp_session_follows_a_recreated_listener()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.UpdateStatusInterval = TimeSpan.FromMilliseconds(300);
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(
            clientOptions: TestHelper.CreateClientOptions(token, channelProtocol: ChannelProtocol.Udp),
            vpnAdapter: TestHelper.CreateTestVpnAdapter());
        await TestHelper.Test_UdpEcho();

        var hostUdpEndPoint = client.RequiredSession.Config.HostUdpEndPoint;
        Assert.IsNotNull(hostUdpEndPoint, "The server must listen on UDP.");
        var serverSession = server.SessionManager.GetSessionById(client.SessionId);
        Assert.IsNotNull(serverSession);
        var transport = serverSession.UdpState.Transport;
        Assert.IsNotNull(transport);
        var oldListener = transport.ChannelTransmitter;

        // a reconfigure with the same endpoint keeps its listener: take it away, then bring it back
        await ReconfigureUdpEndPoints(accessManager, [], TestCt);
        await ReconfigureUdpEndPoints(accessManager, [hostUdpEndPoint], TestCt);

        await TestHelper.Test_UdpEcho();
        Assert.AreSame(transport, serverSession.UdpState.Transport);
        Assert.AreNotSame(oldListener, transport.ChannelTransmitter);
        Assert.IsTrue(serverSession.UseUdpChannel);
    }

    // the client turned to TCP: a copy of one of its older UDP datagrams must not take the session back to UDP
    [TestMethod]
    public async Task An_older_udp_datagram_keeps_a_tcp_session_on_tcp()
    {
        await using var dom = await ClientServerDom.Create(TestHelper,
            TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Udp));
        await TestHelper.Test_UdpEcho();
        Assert.IsTrue(dom.ServerSession.UseUdpChannel);
        var hostUdpEndPoint = dom.Client.RequiredSession.Config.HostUdpEndPoint;
        Assert.IsNotNull(hostUdpEndPoint, "The server must listen on UDP.");

        dom.Client.ChannelProtocol = ChannelProtocol.Tcp;
        await VhTestUtil.AssertEqualsWait(false, () => dom.ServerSession.UseUdpChannel, cancellationToken: TestCt);
        await TestHelper.Test_UdpEcho();
        var tcpChannelCount = dom.ServerSession.TcpChannelCount;

        // genuine, from another socket, with seq 0: never above a seq the client has sent
        using var replayer = new TestClientUdpChannelTransmitter(hostUdpEndPoint, dom.ServerSession.SessionId,
            dom.ServerSession.SessionKey, firstSendSequenceNumber: 0);
        await replayer.UdpTransport.SendAsync(new byte[20]);
        await Task.Delay(500, TestCt);

        Assert.IsFalse(dom.ServerSession.UseUdpChannel);
        Assert.AreEqual(tcpChannelCount, dom.ServerSession.TcpChannelCount);
        await TestHelper.Test_UdpEcho();
    }

    private static async Task ReconfigureUdpEndPoints(TestAccessManager accessManager, IPEndPoint[] udpEndPoints,
        CancellationToken cancellationToken)
    {
        accessManager.ServerConfig.UdpEndPoints = udpEndPoints;
        accessManager.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        await VhTestUtil.AssertEqualsWait(accessManager.ServerConfig.ConfigCode,
            () => accessManager.LastServerStatus?.ConfigCode, cancellationToken: cancellationToken);
    }
}

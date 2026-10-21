using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Dom;
using VpnHood.Test.Extensions;
using VpnHood.Test.Packets;

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
        Assert.IsFalse(dom.ServerSession.Tunnel.UseUdp);
        Assert.AreEqual(tcpChannelCount, dom.ServerSession.TcpChannelCount);
        await UdpTestUtil.AssertNothingReceived(forger, TestCt);
        await TestHelper.Test_UdpEcho();
        Assert.AreEqual(createdChannelCount, clientSession.CreatedPacketChannelCount,
            "The client must still use its first TCP packet channel.");
    }

    // a forged datagram changes nothing; the client's first UDP datagram is its newest, and turns UDP on
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
        Assert.IsFalse(dom.ServerSession.Tunnel.UseUdp);

        // a packet sent while the client is still opening its UDP socket is dropped: wait for the socket first
        dom.Client.ChannelProtocol = ChannelProtocol.Udp;
        await VhTestUtil.AssertEqualsWait(true,
            () => dom.Client.RequiredSession.Status.ActivePacketChannelCount > 0, cancellationToken: TestCt);
        await TestHelper.Test_UdpEcho();
        Assert.IsTrue(dom.ServerSession.Tunnel.UseUdp);
    }

    // the listener dies and comes back on the same endpoint: the client's next datagram moves the session's route to
    // the new one
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
        var udpChannel = serverSession?.UdpChannel;
        Assert.IsNotNull(udpChannel);
        var oldListener = udpChannel.Route?.Transmitter;
        Assert.IsNotNull(oldListener);

        // a reconfigure with the same endpoint keeps its listener: take it away, then bring it back
        await ReconfigureUdpEndPoints(accessManager, [], TestCt);
        await ReconfigureUdpEndPoints(accessManager, [hostUdpEndPoint], TestCt);

        await TestHelper.Test_UdpEcho();
        Assert.AreNotSame(oldListener, udpChannel.Route?.Transmitter);
        Assert.IsTrue(serverSession?.Tunnel.UseUdp);
    }

    // the client turned to TCP: a copy of one of its older UDP datagrams must not take the session back to UDP
    [TestMethod]
    public async Task An_older_udp_datagram_keeps_a_tcp_session_on_tcp()
    {
        await using var dom = await ClientServerDom.Create(TestHelper,
            TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Udp));
        await TestHelper.Test_UdpEcho();
        Assert.IsTrue(dom.ServerSession.Tunnel.UseUdp);
        var hostUdpEndPoint = dom.Client.RequiredSession.Config.HostUdpEndPoint;
        Assert.IsNotNull(hostUdpEndPoint, "The server must listen on UDP.");

        dom.Client.ChannelProtocol = ChannelProtocol.Tcp;
        await VhTestUtil.AssertEqualsWait(false, () => dom.ServerSession.Tunnel.UseUdp, cancellationToken: TestCt);
        await TestHelper.Test_UdpEcho();
        var tcpChannelCount = dom.ServerSession.TcpChannelCount;

        // genuine, from another socket, with seq 0: never above a seq the client has sent
        using var replayer = UdpTestUtil.CreateClientChannel(hostUdpEndPoint, dom.ServerSession.SessionId,
            dom.ServerSession.SessionKey, firstSendSequenceNumber: 0);
        replayer.SendPacketQueued(PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)));
        await Task.Delay(500, TestCt);

        Assert.IsFalse(dom.ServerSession.Tunnel.UseUdp);
        Assert.AreEqual(tcpChannelCount, dom.ServerSession.TcpChannelCount);
        await TestHelper.Test_UdpEcho();
    }

    // A datagram of the client's that never reached the server, sent twice once the client is on TCP: newer than any
    // the server has seen, but older than the client's TCP request, so it must not take the session back to UDP; and
    // a copy goes no further, so the echo server answers its packet once
    [TestMethod]
    public async Task A_lost_udp_datagram_goes_up_once_and_keeps_a_tcp_session_on_tcp()
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

        // the server's listener goes and a socket of the test's takes its endpoint, catching the client's next datagram:
        // the one carrying the test's packet to the echo server
        await ReconfigureUdpEndPoints(accessManager, [], TestCt);
        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        byte[] lostDatagram;
        using (var catcher = new UdpClient(hostUdpEndPoint)) {
            await udpClient.SendAsync(new byte[100], TestHelper.WebServer.MockEps.UdpV4EndPoint1, TestCt);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
            timeoutCts.CancelAfter(TestConstants.DefaultUdpTimeout);
            lostDatagram = (await catcher.ReceiveAsync(timeoutCts.Token)).Buffer;
        }

        // the client turns to TCP before the listener is back, so the server sees none of its datagrams in between
        client.ChannelProtocol = ChannelProtocol.Tcp;
        await VhTestUtil.AssertEqualsWait(false, () => serverSession.Tunnel.UseUdp, cancellationToken: TestCt);
        await ReconfigureUdpEndPoints(accessManager, [hostUdpEndPoint], TestCt);

        using var replayer = new UdpClient(hostUdpEndPoint.AddressFamily);
        await replayer.SendAsync(lostDatagram, hostUdpEndPoint, TestCt);
        await replayer.SendAsync(lostDatagram, hostUdpEndPoint, TestCt);

        // one echo, back on TCP
        using (var echoCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt)) {
            echoCts.CancelAfter(TestConstants.DefaultUdpTimeout);
            await udpClient.ReceiveAsync(echoCts.Token);
        }

        await UdpTestUtil.AssertNothingReceived(udpClient, TestCt);
        Assert.IsFalse(serverSession.Tunnel.UseUdp);
        await TestHelper.Test_UdpEcho();
    }

    // The client turned to TCP: a packet that took its UDP channel as it turned, queued there only now, must not go
    // out, as it would carry the seq of the client's TCP request and take the session back to UDP
    [TestMethod]
    public async Task A_udp_packet_after_the_turn_to_tcp_keeps_a_tcp_session_on_tcp()
    {
        await using var dom = await ClientServerDom.Create(TestHelper,
            TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Udp));
        await TestHelper.Test_UdpEcho();
        Assert.IsTrue(dom.ServerSession.Tunnel.UseUdp);
        var udpChannel = ((ClientSession)dom.Client.RequiredSession).UdpChannel;
        Assert.IsNotNull(udpChannel);

        dom.Client.ChannelProtocol = ChannelProtocol.Tcp;
        await VhTestUtil.AssertEqualsWait(false, () => dom.ServerSession.Tunnel.UseUdp, cancellationToken: TestCt);

        udpChannel.SendPacketQueued(PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)));
        await Task.Delay(500, TestCt);

        Assert.IsFalse(dom.ServerSession.Tunnel.UseUdp);
        await TestHelper.Test_UdpEcho();
    }

    // The client went back to UDP while a TCP request of its was on its way: its datagram at the request's seq came
    // first, so the request must not turn UDP off under it
    [TestMethod]
    public async Task A_late_tcp_request_keeps_a_udp_session_on_udp()
    {
        await using var dom = await ClientServerDom.Create(TestHelper,
            TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Tcp));
        await TestHelper.Test_UdpEcho();
        var clientSession = (ClientSession)dom.Client.RequiredSession;
        var hostUdpEndPoint = clientSession.Config.HostUdpEndPoint;
        Assert.IsNotNull(hostUdpEndPoint, "The server must listen on UDP.");
        Assert.IsNotNull(clientSession.UdpChannel);

        // the datagram at the seq the client's next request carries, from a socket of the test's
        using var sender = UdpTestUtil.CreateClientChannel(hostUdpEndPoint, dom.ServerSession.SessionId,
            dom.ServerSession.SessionKey, firstSendSequenceNumber: clientSession.UdpChannel.NextSendSequenceNumber);
        sender.SendPacketQueued(PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)));
        await VhTestUtil.AssertEqualsWait(true, () => dom.ServerSession.Tunnel.UseUdp, cancellationToken: TestCt);

        // then the request: a channel of the client's goes, and its next packet brings a new one
        var oldChannelIds = dom.ServerSession.Tunnel.GetPacketChannels().Select(x => x.ChannelId).ToArray();
        dom.ServerSession.Tunnel.GetPacketChannels()[0].Dispose();
        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        await VhTestUtil.AssertEqualsWait(true, async () => {
            await udpClient.SendAsync(new byte[100], TestHelper.WebServer.MockEps.UdpV4EndPoint1, TestCt);
            return dom.ServerSession.Tunnel.GetPacketChannels().Any(x => !oldChannelIds.Contains(x.ChannelId));
        }, cancellationToken: TestCt);

        await Task.Delay(500, TestCt);
        Assert.IsTrue(dom.ServerSession.Tunnel.UseUdp);
    }

    // the client turns to UDP with nothing to send: its new socket announces itself, and the session turns to UDP
    [TestMethod]
    public async Task The_turn_to_udp_reaches_the_server_with_nothing_to_send()
    {
        await using var dom = await ClientServerDom.Create(TestHelper,
            TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Tcp));
        await TestHelper.Test_UdpEcho();
        Assert.IsFalse(dom.ServerSession.Tunnel.UseUdp);

        dom.Client.ChannelProtocol = ChannelProtocol.Udp;
        await VhTestUtil.AssertEqualsWait(true, () => dom.ServerSession.Tunnel.UseUdp, cancellationToken: TestCt);
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

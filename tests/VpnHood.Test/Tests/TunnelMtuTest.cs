using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using VpnHood.Core.Client;
using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class TunnelMtuTest : TestBase
{
    // a packet as big as the tunnel's MTU crosses both ways; Steam's games send 1328-byte ones
    [TestMethod]
    [DataRow(ChannelProtocol.Tcp)]
    [DataRow(ChannelProtocol.Udp)]
    public async Task Packet_of_the_tunnel_mtu_crosses_both_ways(ChannelProtocol channelProtocol)
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(
            vpnAdapter: TestHelper.CreateTestVpnAdapter(),
            clientOptions: TestHelper.CreateClientOptions(token, channelProtocol: channelProtocol));
        Assert.AreEqual(TransportDefaults.MtuClient, client.RequiredSession.Config.Mtu);

        // the packet less its IPv4 and UDP headers, less the upload's 4-byte length and the download reply's checksum
        const int byteCount = TransportDefaults.MtuClient - 20 - 8 - 4;
        await TestHelper.Test_UdpUpload(byteCount);
        await TestHelper.Test_UdpDownload(byteCount);
    }

    // a packet of the MTU itself crosses whole; one byte more gets the "too big" reply naming that MTU, so a sender
    // that adapts gets through, and nothing of it crosses
    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public async Task Packet_over_the_mtu_gets_a_reply_naming_it(IpVersion ipVersion)
    {
        const ulong sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();
        using var listener = new TestUdpListener();
        using var serverTunnel = new Tunnel(TestHelper.CreateTunnelOptions(listener.AddSession(sessionId, sessionKey)));
        serverTunnel.UseUdp = true;
        var crossed = new List<IpPacket>();
        serverTunnel.PacketReceived += (_, ipPacket) => {
            lock (crossed) crossed.Add(ipPacket);
        };

        using var clientTunnel = new Tunnel(TestHelper.CreateTunnelOptions(
            UdpTestUtil.CreateClientChannel(listener.LocalEndPoint, sessionId, sessionKey)));
        clientTunnel.UseUdp = true;
        var replies = new List<IpPacket>();
        clientTunnel.PacketReceived += (_, ipPacket) => replies.Add(ipPacket);

        var isV4 = ipVersion == IpVersion.IPv4;
        var source = IPEndPoint.Parse(isV4 ? "10.255.0.2:5000" : "[fd00::2]:5000");
        var destination = IPEndPoint.Parse(isV4 ? "198.18.0.1:27015" : "[2001:db8::1]:27015");
        var headersLength = (isV4 ? 20 : 40) + 8;
        var mtu = clientTunnel.Mtu;

        clientTunnel.SendPacketQueued(PacketBuilder.BuildUdp(source, destination, new byte[mtu - headersLength]));
        await AssertEqualsWait(1, () => {
            lock (crossed) return crossed.Count;
        });
        Assert.AreEqual(mtu, crossed[0].PacketLength);

        clientTunnel.SendPacketQueued(PacketBuilder.BuildUdp(source, destination, new byte[mtu - headersLength + 1]));
        Assert.HasCount(1, replies);
        if (isV4) {
            var icmp = replies[0].ExtractIcmpV4();
            Assert.AreEqual(IcmpV4Type.DestinationUnreachable, icmp.Type);
            Assert.AreEqual((byte)IcmpV4Code.FragmentationNeeded, icmp.Code);
            Assert.AreEqual((uint)mtu, icmp.MessageSpecific);
        }
        else {
            var icmp = replies[0].ExtractIcmpV6();
            Assert.AreEqual(IcmpV6Type.PacketTooBig, icmp.Type);
            Assert.AreEqual((uint)mtu, icmp.MessageSpecific);
        }

        await Task.Delay(300, TestCt);
        lock (crossed) Assert.HasCount(1, crossed);
    }

    // small packets share a datagram only within the batch limit; a packet too big to share goes alone
    [TestMethod]
    [DataRow(new[] { 700, 700 }, new[] { 700, 700 })]
    [DataRow(new[] { 600, 600, 100 }, new[] { 1300 })]
    [DataRow(new[] { 40, 1390, 40 }, new[] { 40, 1390, 40 })]
    [DataRow(new[] { 1400 }, new[] { 1400 })]
    public async Task Udp_channel_shares_a_datagram_within_the_batch_limit(int[] packetLengths, int[] payloadLengths)
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var receiverEndPoint = receiver.Client.LocalEndPoint as IPEndPoint
                               ?? throw new InvalidOperationException("The receiver has no local endpoint.");
        using var transmitter = new UdpChannelTransmitter(new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)),
            new IgnoringDatagramHandler());
        await using var channel = new RoutedUdpPacketChannel(new UdpRoute(transmitter, receiverEndPoint));

        var source = IPEndPoint.Parse("10.255.0.2:5000");
        var destination = IPEndPoint.Parse("198.18.0.1:27015");
        var ipPackets = packetLengths
            .Select(length => PacketBuilder.BuildUdp(source, destination, new byte[length - 28]))
            .ToArray();
        try {
            await channel.Send(ipPackets);
            foreach (var payloadLength in payloadLengths) {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
                timeoutCts.CancelAfter(TestConstants.DefaultUdpTimeout);
                var datagram = await receiver.ReceiveAsync(timeoutCts.Token);
                Assert.AreEqual(UdpDatagram.HeaderLength + payloadLength, datagram.Buffer.Length);
            }

            await UdpTestUtil.AssertNothingReceived(receiver, TestCt);
        }
        finally {
            foreach (var ipPacket in ipPackets)
                ipPacket.Dispose();
        }
    }

    // the server holds a session to the tunnel's MTU even when a client asks for more: its UDP channel carries no
    // bigger packet in one datagram
    [TestMethod]
    [DataRow(1500, TransportDefaults.MtuClient)]
    [DataRow(1300, 1300)]
    public async Task Server_holds_a_session_to_the_tunnel_mtu(int clientMtu, int tunnelMtu)
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(server.ServerHost.TcpEndPoints[0], TestCt);
        await using var stream = new SslStream(tcpClient.GetStream());
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = token.ServerToken.HostName,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }, TestCt);

        var clientInfo = new ClientInfo {
            ClientId = Guid.NewGuid().ToString(),
            ClientVersion = "1.0.0",
            MinProtocolVersion = VpnHoodClientConfig.MinProtocolVersion,
            MaxProtocolVersion = VpnHoodClientConfig.MaxProtocolVersion,
            UserAgent = nameof(TunnelMtuTest)
        };
        var hello = new HelloRequest {
            RequestId = UniqueIdFactory.Create(),
            EncryptedClientId = VhUtils.EncryptClientId(clientInfo.ClientId, token.Secret),
            ClientInfo = clientInfo,
            TokenId = token.TokenId,
            Mtu = clientMtu
        };
        var body = StreamUtils.ObjectToJsonBuffer(hello);
        var header = $"POST / HTTP/1.1\r\nHost: {token.ServerToken.HostName}\r\n" +
                     $"X-ProtocolVersion: 8\r\nX-BinaryStream: None\r\nContent-Length: {body.Length + 2}\r\n\r\n";
        byte[] request = [.. Encoding.ASCII.GetBytes(header), 1, hello.RequestCode, .. body.Span];
        await stream.WriteAsync(request, TestCt);

        using var response = await HttpUtils.ReadResponse(stream, TestCt);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var helloResponse = await StreamUtils.ReadObjectAsync<HelloResponse>(stream, TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, helloResponse.ErrorCode);
        Assert.AreEqual(tunnelMtu, helloResponse.TunnelMtu);
    }

    [TestMethod]
    public void Tunnel_mtu_is_the_servers_but_never_above_the_clients()
    {
        var helloResponse = new HelloResponse { ErrorCode = SessionErrorCode.Ok, TunnelMtu = 1300 };
        Assert.AreEqual(1300, ClientHelper.GetTunnelMtu(1400, helloResponse));

        helloResponse.TunnelMtu = 1500;
        Assert.AreEqual(1400, ClientHelper.GetTunnelMtu(1400, helloResponse));
    }

    // an older server sends no TunnelMtu and carries a packet up to the smaller MTU less its overhead; an adapter above
    // that would get full-size TCP segments sent that the server drops
    [TestMethod]
    public void Older_server_keeps_its_own_limit()
    {
        var helloResponse = new HelloResponse { ErrorCode = SessionErrorCode.Ok, Mtu = TransportDefaults.MtuServer };
        Assert.IsNull(helloResponse.TunnelMtu);
        Assert.AreEqual(1280, ClientHelper.GetTunnelMtu(TransportDefaults.MtuClient, helloResponse));
    }

    // a channel whose datagrams go to a plain socket, so a test sees each one as it went
    private sealed class RoutedUdpPacketChannel(UdpRoute route)
        : UdpPacketChannel(sessionId: 1UL, VhUtils.GenerateKey(), isServer: false, firstSendSequenceNumber: 1UL,
            trafficMeter: null, isLegacy: false)
    {
        protected override UdpRoute GetRoute() => route;
        public ValueTask Send(IReadOnlyList<IpPacket> ipPackets) => SendPacketsAsync(ipPackets);
    }

    private sealed class IgnoringDatagramHandler : IUdpDatagramHandler
    {
        public void OnDatagramReceived(UdpChannelTransmitter transmitter, Memory<byte> datagram,
            IPEndPoint remoteEndPoint)
        {
        }
    }
}

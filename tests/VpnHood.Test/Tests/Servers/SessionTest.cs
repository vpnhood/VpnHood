using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.DatagramMessaging;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests.Servers;

[TestClass]
public class SessionTest : TestBase
{
    // connects the client over loopback and returns the server's end
    private async Task<TcpStreamConnection> Accept(TcpClient client)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try {
            var acceptTask = listener.AcceptTcpClientAsync(TestCt).AsTask();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, TestCt);
            return new TcpStreamConnection(await acceptTask, connectionName: "tunnel", isServer: true);
        }
        finally {
            listener.Stop();
        }
    }

    private static Session CreateSession(IAccessManager accessManager, NetFilter netFilter,
        SessionOptions? options = null)
    {
        return new Session(accessManager, vpnAdapter: null, new TestSocketFactory(), netFilter,
            new SessionResponseEx { ErrorCode = SessionErrorCode.Ok, SessionId = 1, SessionKey = new byte[16] },
            options ?? new SessionOptions(), new TrackingOptions(), new SessionExtraData(),
            new VirtualIpBundle { IpV4 = IPAddress.Parse("10.10.0.2"), IpV6 = IPAddress.Parse("fd00::2") });
    }

    // the request as the server reads it, from what the client writes
    private async Task<T> ReadAsServer<T>(T request) where T : ClientRequest
    {
        using var stream = new MemoryStream(StreamUtils.ObjectToJsonBuffer(request).ToArray());
        return await StreamUtils.ReadObjectAsync<T>(stream, TestCt);
    }

    [TestMethod]
    public async Task Channel_request_closes_the_older_channels_the_client_no_longer_lists()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        using var netFilter = new NetFilter();
        using var session = CreateSession(accessManager, netFilter);

        // an older channel, which the client's next request does not list, as it is replacing it
        using var oldClient = new TcpClient();
        var oldChannel = new StreamPacketChannel(new StreamPacketChannelOptions {
            StreamConnection = await Accept(oldClient),
            RequestTime = DateTime.UtcNow - TimeSpan.FromMinutes(1),
            ChannelId = "old",
            AutoDisposePackets = true,
            Blocking = false,
            Lifespan = null
        });
        session.Tunnel.AddChannel(oldChannel);

        using var newClient = new TcpClient();
        await session.ProcessTcpPacketChannelRequest(new TcpPacketChannelRequest {
            RequestId = "new",
            ChannelId = "new",
            SessionId = session.SessionId,
            SessionKey = session.SessionKey,
            ActiveChannelIds = []
        }, await Accept(newClient), TestCt);

        // closed, not disposed: it goes after its grace, in which what is on its way arrives, and its close goes out
        Assert.AreEqual(PacketChannelState.Disconnecting, oldChannel.State);
        using var reader = new StreamPacketReader(oldClient.GetStream(), bufferSize: 1500);
        using var sent = await reader.ReadAsync(TestCt).WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsNotNull(sent);
        Assert.IsInstanceOfType<ClosePacketMessage>(PacketMessageHandler.ReadMessage(sent));
        Assert.AreEqual(PacketChannelState.Disconnecting, oldChannel.State);
    }

    // a request a network change cut off, which reaches the server after the channel that replaced it
    [TestMethod]
    public async Task Late_request_leaves_the_newer_channels_open()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        using var netFilter = new NetFilter();
        using var session = CreateSession(accessManager, netFilter);

        using var newerClient = new TcpClient();
        var newerChannel = new StreamPacketChannel(new StreamPacketChannelOptions {
            StreamConnection = await Accept(newerClient),
            RequestTime = DateTime.UtcNow,
            ChannelId = "newer",
            AutoDisposePackets = true,
            Blocking = false,
            Lifespan = null
        });
        session.Tunnel.AddChannel(newerChannel);

        // made before the newer channel's request, so it lists nothing of it
        var requestTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        var lateRequest = await ReadAsServer(new TcpPacketChannelRequest {
            RequestId = "late",
            ChannelId = "late",
            SessionId = session.SessionId,
            SessionKey = session.SessionKey,
            ActiveChannelIds = [],
            RequestTime = requestTime
        });
        Assert.AreEqual(requestTime, lateRequest.RequestTime);
        using var lateClient = new TcpClient();
        await session.ProcessTcpPacketChannelRequest(lateRequest, await Accept(lateClient), TestCt);

        Assert.AreEqual(PacketChannelState.Connected, newerChannel.State);
    }

    // the users' UDP that an app sends anyway, though the hello told the client to drop it: dropped only with Cloak
    // Mode, and DNS always passes
    [TestMethod]
    [DataRow(false, false, true, new[] { 53 })]
    [DataRow(false, true, false, new[] { 1234, 53 })]
    [DataRow(true, false, false, new[] { 1234, 443, 80, 53 })]
    public void Udp_drop_stops_what_an_app_sends_anyway(bool allowTcpPacket, bool allowUdpPacket,
        bool allowQuicPacket, int[] passedPorts)
    {
        using var accessManager = TestHelper.CreateAccessManager();
        var ipFilter = new PortRecorderFilter();
        using var netFilter = new NetFilter { IpFilter = ipFilter };
        using var session = CreateSession(accessManager, netFilter, new SessionOptions {
            AllowTcpPacket = allowTcpPacket, AllowUdpPacket = allowUdpPacket, AllowQuicPacket = allowQuicPacket
        });
        using var channel = new TestPacketChannel();
        session.Tunnel.AddChannel(channel);

        foreach (var port in new[] { 1234, 443, 80, 53 })
            channel.Receive(PacketBuilder.BuildUdp(session.VirtualIps.IpV4, IPAddress.Parse("192.0.2.1"), 5000, port,
                new byte[16]));

        CollectionAssert.AreEqual(passedPorts, ipFilter.Ports);
    }

    // records the port of each packet that reaches it, and blocks it, so nothing leaves the test
    private sealed class PortRecorderFilter : IIpFilter
    {
        public List<int> Ports { get; } = [];
        public event EventHandler? Changed { add { } remove { } }
        public void Reconfigure() { }
        public bool IsEmpty => false;

        public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
        {
            Ports.Add(endPoint.Port);
            return FilterAction.Block;
        }

        public void Dispose() { }
    }
}

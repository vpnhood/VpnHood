using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.DatagramMessaging;
using VpnHood.Core.Tunneling.Messaging;
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

    [TestMethod]
    public async Task Channel_request_closes_the_older_channels_the_client_no_longer_lists()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        using var netFilter = new NetFilter();
        using var session = new Session(accessManager, vpnAdapter: null, new TestSocketFactory(), netFilter,
            new SessionResponseEx { ErrorCode = SessionErrorCode.Ok, SessionId = 1, SessionKey = new byte[16] },
            new SessionOptions(), new TrackingOptions(), new SessionExtraData(),
            new VirtualIpBundle { IpV4 = IPAddress.Parse("10.10.0.2"), IpV6 = IPAddress.Parse("fd00::2") });

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
}

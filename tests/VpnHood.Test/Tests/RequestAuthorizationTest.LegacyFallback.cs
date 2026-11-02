using VpnHood.Core.Client;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Test.Tests;

// A server before protocol 15 answers an app's HTTP 15 with a 401, which these servers do with their newest
// HTTP version at 14.
public partial class RequestAuthorizationTest
{
    [TestMethod]
    [DoNotParallelize] // The proxy stream observed must be this test's own.
    public async Task A_new_app_speaks_the_legacy_protocol_to_a_server_before_15_and_pads_nothing()
    {
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.SessionOptions.StartPadding = true;
        await using var server = await TestHelper.CreateServer(options);
        server.ServerHost.MaxHttpProtocolVersion = 14;
        var token = await TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(clientOptions:
            TestHelper.CreateClientOptions(token, channelProtocol: ChannelProtocol.Udp));
        var session = (ClientSession)client.RequiredSession;
        Assert.IsTrue(ServerProtocolMemory.IsLegacy(session.VpnEndPoint.TcpEndPoint, GetFarmSecret(token)));
        await TestHelper.Test_Https();

        // Padding rides on HTTP 15's masking: none, though the server's options ask for it.
        var request = new StreamProxyChannelRequest {
            RequestId = UniqueIdFactory.Create(),
            SessionId = session.Config.SessionId,
            SessionKey = session.Config.SessionKey,
            DestinationEndPoint = MockEps.TcpUploadEndPoint1
        };
        await using var result = await session.SendRequest<SessionResponse>(request, TestCt);
        var stream = result.StreamConnection.Stream as WebSocketStream ??
                     throw new InvalidOperationException("Expected a WebSocket proxy stream.");
        Assert.IsFalse(stream.UsesStandardMasking);
        Assert.IsFalse(stream.HasWrittenInitialPadding);
        await stream.WriteAsync(new byte[4], TestCt);
        var checksum = new byte[4];
        await stream.ReadExactlyAsync(checksum, TestCt);
        CollectionAssert.AreEqual(new byte[4], checksum);
    }

    [TestMethod]
    public async Task A_server_that_took_15_is_never_asked_the_legacy_way()
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token));
        var endPoint = ((ClientSession)client.RequiredSession).VpnEndPoint.TcpEndPoint;
        Assert.IsFalse(ServerProtocolMemory.IsLegacy(endPoint, GetFarmSecret(token)));

        // The same server now answers 15 with a 401, as could anything that ends its TLS; the app keeps the proof.
        server.ServerHost.MaxHttpProtocolVersion = 14;
        await using var client2 = await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token),
            autoConnect: false);
        await Assert.ThrowsAsync<Exception>(() => client2.Connect(TestCt));
        Assert.IsFalse(ServerProtocolMemory.IsLegacy(endPoint, GetFarmSecret(token)));
    }

    [TestMethod]
    public async Task A_server_that_took_only_the_legacy_way_is_asked_15_once_it_refuses_that()
    {
        await using var server = await TestHelper.CreateServer();
        server.ServerHost.MaxHttpProtocolVersion = 14;
        var token = await TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token));
        var endPoint = ((ClientSession)client.RequiredSession).VpnEndPoint.TcpEndPoint;
        Assert.IsTrue(ServerProtocolMemory.IsLegacy(endPoint, GetFarmSecret(token)));
        await client.DisposeAsync(); // the traffic below must be the second client's

        // Updated, and set to refuse the apps before 15.
        server.ServerHost.MaxHttpProtocolVersion = ServerHost.MaxProtocolVersion;
        server.SessionManager.SessionOptions.MinClientProtocolVersion = 15;
        await using var client2 = await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token));
        Assert.IsFalse(ServerProtocolMemory.IsLegacy(endPoint, GetFarmSecret(token)));
        await TestHelper.Test_Https();
    }

    private static byte[] GetFarmSecret(Token token) =>
        token.ServerToken.Secret ?? throw new InvalidOperationException("The test token has no farm secret.");
}

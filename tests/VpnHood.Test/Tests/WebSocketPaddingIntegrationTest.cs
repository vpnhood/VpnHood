using System.Text.Json;
using VpnHood.Core.Client;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Test.Extensions;

namespace VpnHood.Test.Tests;

[TestClass]
public class WebSocketPaddingIntegrationTest : TestBase
{
    [TestMethod]
    public void Padding_defaults_off_and_explicit_false_overrides_an_inherited_setting()
    {
        var options = new SessionOptions();
        Assert.IsFalse(options.StartPaddingValue);
        options.Merge(new SessionOptions { StartPadding = true });
        Assert.IsTrue(options.StartPaddingValue);
        options.Merge(new SessionOptions());
        Assert.IsTrue(options.StartPaddingValue);
        options.Merge(new SessionOptions { StartPadding = false });
        Assert.IsFalse(options.StartPaddingValue);
        Assert.IsFalse(JsonSerializer.Deserialize<HelloResponse>("{\"ErrorCode\":0}")?.StartPadding);
        Assert.IsFalse(JsonSerializer.Serialize(new HelloResponse { ErrorCode = SessionErrorCode.Ok }).Contains("StartPadding"));
    }

    [TestMethod]
    [DoNotParallelize] // Observe exact reuse counts without unrelated traffic through the test VPN.
    [DataRow(false)]
    [DataRow(true)]
    public async Task Forwarding_transfers_and_reuse_work_with_padding_off_and_on(bool enabled)
    {
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.SessionOptions.StartPadding = enabled;
        await using var server = await TestHelper.CreateServer(options);
        var token = await TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(clientOptions:
            TestHelper.CreateClientOptions(token, channelProtocol: ChannelProtocol.Udp));
        Assert.AreEqual(enabled, server.SessionManager.GetSessionById(client.SessionId)?.StartPadding);

        await TestHelper.Test_Https();
        await AssertEqualsWait(1, () => client.RequiredSession.Status.ConnectorStatus.FreeConnectionCount);
        var created = client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount;
        var reused = client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount;
        for (var i = 0; i < 3; i++) {
            await TestHelper.Test_Https();
            await AssertEqualsWait(1, () => client.RequiredSession.Status.ConnectorStatus.FreeConnectionCount);
            Assert.AreEqual(created, client.RequiredSession.Status.ConnectorStatus.CreatedConnectionCount);
            Assert.AreEqual(++reused, client.RequiredSession.Status.ConnectorStatus.ReusedConnectionSucceededCount);
        }
        // Larger transfers outlive the initial framing and check both directions for data corruption.
        await TestHelper.Test_TcpUpload(200_000);
        await TestHelper.Test_TcpDownload(200_000);
    }

    [TestMethod]
    [DoNotParallelize] // Unrelated VPN traffic could take the connection whose reuse this test observes.
    [DataRow(false)]
    [DataRow(true)]
    public async Task Real_client_hello_controls_forwarding_masking_and_emitted_padding(bool enabled)
    {
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.SessionOptions.StartPadding = enabled;
        await using var server = await TestHelper.CreateServer(options);
        var token = await TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(clientOptions:
            TestHelper.CreateClientOptions(token, channelProtocol: ChannelProtocol.Udp));
        var session = (ClientSession)client.RequiredSession;
        Assert.AreEqual(enabled, server.SessionManager.GetSessionById(client.SessionId)?.StartPadding);

        // The real hello must configure the sender; enabling its padding here would hide a broken hand-off.
        for (var i = 0; i < 3; i++) {
            await AssertForwardingMaskingAndPadding(session, enabled, i, TestCt);

            await AssertEqualsWait(i == 0 ? 1 : 0,
                () => client.RequiredSession.Status.ConnectorStatus.FreeConnectionCount);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Packet_channels_and_plain_streams_work_when_padding_is_enabled(bool useWebSocket)
    {
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.SessionOptions.StartPadding = true;
        await using var server = await TestHelper.CreateServer(options);
        var token = await TestHelper.CreateAccessToken(server);
        var clientOptions = TestHelper.CreateClientOptions(token, channelProtocol: ChannelProtocol.Tcp);
        if (!useWebSocket)
            clientOptions.DebugData1 = "/disable-WebSocket";
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions);
        await TestHelper.Test_UdpEcho();
        await TestHelper.Test_Https();
    }

    private async Task AssertForwardingMaskingAndPadding(ClientSession session, bool enabled, int streamIndex,
        CancellationToken cancellationToken)
    {
        var request = new TcpForwardingChannelRequest {
            RequestId = UniqueIdFactory.Create(),
            SessionId = session.Config.SessionId,
            SessionKey = session.Config.SessionKey,
            DestinationEndPoint = MockEps.TcpUploadEndPoint1
        };
        await using var result = await session.SendRequest<SessionResponse>(request, cancellationToken);
        var stream = result.StreamConnection.Stream as WebSocketStream ??
                     throw new InvalidOperationException("Expected a WebSocket forwarding stream.");
        Assert.AreEqual(streamIndex == 1 ? 1 : 0, stream.ReusedCount);
        Assert.AreEqual(enabled, stream.UsesStandardMasking, $"Masking offer on forwarding stream {streamIndex}.");
        Assert.AreEqual(enabled, stream.HasWrittenInitialPadding, $"Client padding on forwarding stream {streamIndex}.");

        // A zero-byte upload produces a checksum without relying on a cover timer.
        await stream.WriteAsync(new byte[4], cancellationToken);
        var checksum = new byte[4];
        await stream.ReadExactlyAsync(checksum, cancellationToken);
        CollectionAssert.AreEqual(new byte[4], checksum);

        // Reuse the first transport once, then require another actual upgrade.
        if (streamIndex >= 1)
            result.StreamConnection.PreventReuse();
    }
}

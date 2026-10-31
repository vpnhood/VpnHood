using System.Text.Json;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Tunneling.Messaging;
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
    public async Task Proxy_transfers_and_reuse_work_with_padding_off_and_on(bool enabled)
    {
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.SessionOptions.StartPadding = enabled;
        await using var server = await TestHelper.CreateServer(options);
        var token = TestHelper.CreateAccessToken(server);
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
        await TestHelper.Test_TcpUpload(200_000, cancellationToken: TestCt);
        await TestHelper.Test_TcpDownload(200_000, cancellationToken: TestCt);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Packet_channels_and_plain_streams_work_when_padding_is_enabled(bool useWebSocket)
    {
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.SessionOptions.StartPadding = true;
        await using var server = await TestHelper.CreateServer(options);
        var token = TestHelper.CreateAccessToken(server);
        var clientOptions = TestHelper.CreateClientOptions(token, channelProtocol: ChannelProtocol.Tcp);
        if (!useWebSocket)
            clientOptions.DebugData1 = "/disable-WebSocket";
        await using var client = await TestHelper.CreateClient(clientOptions: clientOptions);
        await TestHelper.Test_UdpEcho();
        await TestHelper.Test_Https();
    }
}

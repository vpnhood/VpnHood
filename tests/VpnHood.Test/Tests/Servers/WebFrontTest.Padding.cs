using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.ProbeResistance;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Core.Tunneling.WebSockets;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests.Servers;

public partial class WebFrontTest
{
    [TestMethod]
    [DataRow(15, true, true)]
    [DataRow(15, false, true)] // An early protocol-15 client still uses legacy mask handling.
    [DataRow(14, true, true)]
    [DataRow(15, true, false)]
    public async Task Padding_hello_and_upgrade_preserve_legacy_compatibility(int httpVersion, bool offerMasking,
        bool enabled)
    {
        using var accessManager = await TestHelper.CreateAccessManager(TestCt);
        accessManager.ServerConfig.SessionOptions.StartPadding = enabled;
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        var clientInfo = new ClientInfo {
            ClientId = Guid.NewGuid().ToString(), ClientVersion = "1.0.0", MinProtocolVersion = 8,
            MaxProtocolVersion = 15, UserAgent = "PaddingTest"
        };
        var hello = new HelloRequest {
            RequestId = UniqueIdFactory.Create(), ClientInfo = clientInfo, TokenId = token.TokenId,
            EncryptedClientId = VhUtils.EncryptClientId(clientInfo.ClientId, token.Secret)
        };
        var secret = token.ServerToken.Secret ?? throw new InvalidOperationException("Missing farm secret.");
        var proof = httpVersion >= 15 ? new RequestProof(secret).Create(httpVersion, DateTimeOffset.UtcNow) : null;
        var body = StreamUtils.ObjectToJsonBuffer(hello);
        using var helloSocket = new TcpClient();
        await using var helloTls = await ConnectTls(helloSocket, token);
        var head = BuildAuthenticationHead(token.ServerToken.HostName, false, httpVersion, proof, body.Length + 2);
        byte[] helloPayload = [.. Encoding.ASCII.GetBytes(head), 1, hello.RequestCode, .. body.Span];
        await helloTls.WriteAsync(helloPayload, TestCt);
        using var helloHttp = await HttpUtils.ReadResponse(helloTls, TestCt);
        Assert.AreEqual(HttpStatusCode.OK, helloHttp.StatusCode);
        var helloJson = await StreamUtils.ReadMessageAsync(helloTls, TestCt);
        var helloResponse = JsonSerializer.Deserialize<HelloResponse>(helloJson);
        Assert.IsNotNull(helloResponse);
        Assert.AreEqual(SessionErrorCode.Ok, helloResponse.ErrorCode);
        Assert.AreEqual(enabled && httpVersion >= 15, helloResponse.StartPadding);

        using var proxySocket = new TcpClient();
        await using var proxyTls = await ConnectTls(proxySocket, token);
        var upgrade = BuildAuthenticationHead(token.ServerToken.HostName, true, httpVersion, proof, 0);
        if (offerMasking)
            upgrade = upgrade[..^2] + "X-WebSocketMasking: true\r\n\r\n";
        await proxyTls.WriteAsync(Encoding.ASCII.GetBytes(upgrade), TestCt);
        using var upgradeResponse = await HttpUtils.ReadResponse(proxyTls, TestCt);
        Assert.AreEqual(HttpStatusCode.SwitchingProtocols, upgradeResponse.StatusCode);
        var standardMasking = httpVersion >= 15 && offerMasking;
        Assert.AreEqual(standardMasking, upgradeResponse.Headers.Contains("X-WebSocketMasking"));

        // This writer intentionally has no read ownership: inspect the server's frames below the wrapper.
        using var writer = new WebSocketStream(proxyTls, "padding-proxy", false, false, standardMasking);
        var proxy = new StreamProxyChannelRequest {
            RequestId = UniqueIdFactory.Create(), SessionId = helloResponse.SessionId,
            SessionKey = helloResponse.SessionKey, DestinationEndPoint = MockEps.TcpUploadEndPoint1
        };
        var proxyBody = StreamUtils.ObjectToJsonBuffer(proxy);
        // A zero-byte upload makes the destination return a checksum, even when no text padding is sent.
        byte[] proxyPayload = [1, proxy.RequestCode, .. proxyBody.Span, 0, 0, 0, 0];
        await writer.WriteAsync(proxyPayload, TestCt);
        var responseHeader = await WebSocketUtils.ReadWebSocketHeader(proxyTls, new byte[14], TestCt);
        Assert.IsTrue(responseHeader.IsBinary);
        var responseBody = new byte[(int)responseHeader.PayloadLength];
        await proxyTls.ReadExactlyAsync(responseBody, TestCt);
        using var responseStream = new MemoryStream(responseBody);
        var responseJson = await StreamUtils.ReadMessageAsync(responseStream, TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, JsonSerializer.Deserialize<SessionResponse>(responseJson)?.ErrorCode);
        var next = await WebSocketUtils.ReadWebSocketHeader(proxyTls, new byte[14], TestCt);
        Assert.AreEqual(enabled && standardMasking, next.IsText,
            "Only a capable peer receives padding after the server's initial session response.");
        var nextPayload = new byte[(int)next.PayloadLength];
        await proxyTls.ReadExactlyAsync(nextPayload, TestCt);
        if (next.IsText)
            Assert.IsTrue(nextPayload.All(x => x is >= 32 and <= 126));
        else
            CollectionAssert.AreEqual(new byte[4], nextPayload);
    }
}

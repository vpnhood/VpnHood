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
    [DataRow(15, false, true)]
    [DataRow(14, true, true)]
    [DataRow(15, true, false)]
    public async Task Padding_requires_session_and_request_masking_on_new_and_reused_streams(int httpVersion, bool offerMasking,
        bool enabled)
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        accessManager.ServerConfig.SessionOptions.StartPadding = enabled;
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = await TestHelper.CreateAccessToken(server);
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
        Assert.IsFalse(upgradeResponse.Headers.Contains("X-WebSocketMasking"));

        // Reading below the wrapper exposes text padding that WebSocketStream normally discards.
        await using var writer = new WebSocketStream(proxyTls, "padding-proxy", false, false, standardMasking);
        for (var streamIndex = 0; streamIndex < 2; streamIndex++)
            await AssertProxyStreamPadding(writer, proxyTls, helloResponse, enabled && standardMasking,
                streamIndex, TestCt);
    }

    private async Task AssertProxyStreamPadding(WebSocketStream writer, Stream transport, HelloResponse helloResponse,
        bool expectedPadding, int streamIndex, CancellationToken cancellationToken)
    {
        var proxy = new StreamProxyChannelRequest {
            RequestId = UniqueIdFactory.Create(), SessionId = helloResponse.SessionId,
            SessionKey = helloResponse.SessionKey, DestinationEndPoint = MockEps.TcpUploadEndPoint1
        };
        var proxyBody = StreamUtils.ObjectToJsonBuffer(proxy);
        // A zero-byte upload makes the destination return a checksum, even when no text padding is sent.
        byte[] proxyPayload = [1, proxy.RequestCode, .. proxyBody.Span, 0, 0, 0, 0];
        await writer.WriteAsync(proxyPayload, cancellationToken);
        var responseHeader = await WebSocketUtils.ReadWebSocketHeader(transport, new byte[14], cancellationToken);
        Assert.IsTrue(responseHeader.IsBinary);
        var responseBody = new byte[(int)responseHeader.PayloadLength];
        await transport.ReadExactlyAsync(responseBody, cancellationToken);
        using var responseStream = new MemoryStream(responseBody);
        var responseJson = await StreamUtils.ReadMessageAsync(responseStream, cancellationToken);
        Assert.AreEqual(SessionErrorCode.Ok, JsonSerializer.Deserialize<SessionResponse>(responseJson)?.ErrorCode);

        var next = await WebSocketUtils.ReadWebSocketHeader(transport, new byte[14], cancellationToken);
        Assert.AreEqual(expectedPadding, next.IsText, $"Padding on logical stream {streamIndex}.");
        using var checksum = new MemoryStream();
        var sentTerminator = false;
        while (true) {
            Assert.IsTrue(next.IsBinary || next.IsText);
            Assert.IsTrue(next.MaskKey.IsEmpty);
            if (next is { IsBinary: true, PayloadLength: 0 })
                break;

            var payload = new byte[(int)next.PayloadLength];
            await transport.ReadExactlyAsync(payload, cancellationToken);
            if (next.IsText) {
                Assert.IsTrue(expectedPadding);
                Assert.IsTrue(payload.All(x => x is >= 32 and <= 126));
            }
            else {
                checksum.Write(payload);
                Assert.IsTrue(checksum.Length <= 4);
            }

            if (checksum.Length == 4 && !sentTerminator) {
                // End only after the destination's reply: closing earlier would end its proxy pump.
                await transport.WriteAsync(new byte[] { 0x82, 0x80, 0, 0, 0, 0 }, cancellationToken);
                sentTerminator = true;
            }
            next = await WebSocketUtils.ReadWebSocketHeader(transport, new byte[14], cancellationToken);
        }
        Assert.IsTrue(sentTerminator);
        CollectionAssert.AreEqual(new byte[4], checksum.ToArray());
    }
}

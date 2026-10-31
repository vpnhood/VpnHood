using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using VpnHood.Core.Client;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.WebSockets;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Test.Tests;

public partial class RequestAuthorizationTest
{
    [TestMethod]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public async Task Padding_requires_both_session_opt_in_and_upgrade_agreement_and_excludes_packets(
        bool peerSupportsMasking, bool enabled, bool packet)
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16));
        var connector = CreateConnector((IPEndPoint)listener.LocalEndpoint, certificate, signer, useWebSocket: true);
        connector.StartPadding = enabled;
        await using var sender = new RequestSender(connector, signer);
        ClientRequest request = packet
            ? new TcpPacketChannelRequest { RequestId = "packet", SessionId = 1, SessionKey = new byte[16] }
            : new StreamProxyChannelRequest {
                RequestId = "proxy", SessionId = 1, SessionKey = new byte[16],
                DestinationEndPoint = new IPEndPoint(IPAddress.Loopback, 443)
            };
        var pending = sender.SendRequest<SessionResponse>(request, TestCt);
        using var socket = await listener.AcceptTcpClientAsync(TestCt);
        await using var tls = new SslStream(socket.GetStream());
        var headers = await ReadRequest(tls, certificate, useWebSocket: true, TestCt);
        Assert.AreEqual("true", headers["X-WebSocketMasking"]);
        var maskingHeader = peerSupportsMasking ? "X-WebSocketMasking: true\r\n" : "";
        await tls.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\nConnection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {WebSocketUtils.ComputeWebSocketAccept(headers["Sec-WebSocket-Key"])}\r\n" +
            maskingHeader + "\r\n"), TestCt);

        // Read frames directly to check the actual bytes and the first frame following the request.
        var first = await ReadPaddingTestFrame(tls, peerSupportsMasking, TestCt);
        Assert.IsTrue(first.Header.IsBinary);
        CollectionAssert.AreEqual(new byte[] { 1, request.RequestCode }, first.Payload[..2]);
        using var body = new MemoryStream(first.Payload[2..]);
        var json = await StreamUtils.ReadMessageAsync(body, TestCt);
        StringAssert.Contains(json, request.RequestId);
        var response = StreamUtils.ObjectToJsonBuffer(new SessionResponse { ErrorCode = SessionErrorCode.Ok });
        var frame = new byte[response.Length + 14];
        var headerLength = WebSocketUtils.BuildWebSocketFrameHeader(frame, response.Length).Length;
        response.CopyTo(frame.AsMemory(headerLength));
        await tls.WriteAsync(frame.AsMemory(0, headerLength + response.Length), TestCt);
        await using var result = await pending;
        // A second real write is a delimiter when padding is off, avoiding a timeout-based absence check.
        await result.StreamConnection.Stream.WriteAsync(new byte[] { 42 }, TestCt);
        var next = await ReadPaddingTestFrame(tls, peerSupportsMasking, TestCt);
        var expectPadding = enabled && peerSupportsMasking && !packet;
        Assert.AreEqual(expectPadding, next.Header.IsText);
        if (expectPadding)
            Assert.IsTrue(next.Payload.All(x => x is >= 32 and <= 126));
        else
            CollectionAssert.AreEqual(new byte[] { 42 }, next.Payload);
        result.StreamConnection.PreventReuse();
    }

    private static async Task<(WebSocketHeader Header, byte[] Payload)> ReadPaddingTestFrame(Stream stream,
        bool unmask, CancellationToken cancellationToken)
    {
        var header = await WebSocketUtils.ReadWebSocketHeader(stream, new byte[14], cancellationToken);
        var payload = new byte[(int)header.PayloadLength];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        if (unmask && !header.MaskKey.IsEmpty)
            for (var i = 0; i < payload.Length; i++)
                payload[i] ^= header.MaskKey.Span[i & 3];
        return (header, payload);
    }
}

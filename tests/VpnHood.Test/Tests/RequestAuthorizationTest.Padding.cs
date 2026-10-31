using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using VpnHood.Core.Client;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.WebSockets;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Test.Tests;

public partial class RequestAuthorizationTest
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public async Task Hello_controls_masking_and_padding_while_packets_stay_unmasked(
        bool enabled, bool packet, bool responseEcho)
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16));
        var connector = CreateConnector((IPEndPoint)listener.LocalEndpoint, certificate, signer, useWebSocket: true);
        await using var sender = new RequestSender(connector, signer);
        await CompletePaddingHello(connector, sender, listener, certificate, enabled, responseEcho, TestCt);

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
        var expectPadding = enabled && !packet;
        Assert.AreEqual(expectPadding, headers.ContainsKey("X-WebSocketMasking"));
        if (expectPadding)
            Assert.AreEqual("true", headers["X-WebSocketMasking"]);
        await WritePaddingUpgrade(tls, headers, responseEcho, TestCt);

        var first = await ReadPaddingTestFrame(tls, expectPadding, TestCt);
        Assert.IsTrue(first.Header.IsBinary);
        CollectionAssert.AreEqual(new byte[] { 1, request.RequestCode }, first.Payload[..2]);
        using var body = new MemoryStream(first.Payload[2..]);
        var json = await StreamUtils.ReadMessageAsync(body, TestCt);
        StringAssert.Contains(json, request.RequestId);
        await WritePaddingTestResponse(tls, new SessionResponse { ErrorCode = SessionErrorCode.Ok }, TestCt);
        await using var result = await pending;
        Assert.AreEqual(expectPadding, ((WebSocketStream)result.StreamConnection.Stream).UsesStandardMasking);

        // A real frame bounds the absence check without waiting for a timer.
        await result.StreamConnection.Stream.WriteAsync(new byte[] { 42 }, TestCt);
        var next = await ReadPaddingTestFrame(tls, expectPadding, TestCt);
        Assert.AreEqual(expectPadding, next.Header.IsText);
        if (expectPadding)
            Assert.IsTrue(next.Payload.All(x => x is >= 32 and <= 126));
        else
            CollectionAssert.AreEqual(new byte[] { 42 }, next.Payload);
        result.StreamConnection.PreventReuse();
    }

    [TestMethod]
    public async Task Server_checks_before_hello_ignore_unsolicited_masking_echo()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16));
        await using var sender = new RequestSender(CreateConnector((IPEndPoint)listener.LocalEndpoint,
            certificate, signer, useWebSocket: true), signer);
        var pending = sender.SendRequest<SessionResponse>(new ServerCheckRequest { RequestId = "check" }, TestCt);
        using var socket = await listener.AcceptTcpClientAsync(TestCt);
        await using var tls = new SslStream(socket.GetStream());
        var headers = await ReadRequest(tls, certificate, useWebSocket: true, TestCt);
        Assert.IsFalse(headers.ContainsKey("X-WebSocketMasking"));
        await WritePaddingUpgrade(tls, headers, responseEcho: true, TestCt);
        var first = await ReadPaddingTestFrame(tls, unmask: false, TestCt);
        CollectionAssert.AreEqual(new byte[] { 1, (byte)RequestCode.ServerCheck }, first.Payload[..2]);
        await WritePaddingTestResponse(tls, new SessionResponse { ErrorCode = SessionErrorCode.Ok }, TestCt);
        await using var result = await pending;
        Assert.IsFalse(((WebSocketStream)result.StreamConnection.Stream).UsesStandardMasking);
    }

    private static async Task CompletePaddingHello(ConnectorService connector, RequestSender sender, TcpListener listener,
        X509Certificate2 certificate, bool enabled, bool responseEcho, CancellationToken cancellationToken)
    {
        var pending = sender.SendRequest<HelloResponse>(new HelloRequest {
            RequestId = "hello-padding",
            TokenId = "token",
            EncryptedClientId = [],
            ClientInfo = new ClientInfo {
                ClientId = "padding-client", ClientVersion = "1.0.0", UserAgent = "test",
                MinProtocolVersion = 15, MaxProtocolVersion = 15
            }
        }, cancellationToken);
        using var socket = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var tls = new SslStream(socket.GetStream());
        var headers = await ReadRequest(tls, certificate, useWebSocket: true, cancellationToken);
        Assert.IsFalse(headers.ContainsKey("X-WebSocketMasking"));
        await WritePaddingUpgrade(tls, headers, responseEcho, cancellationToken);
        var first = await ReadPaddingTestFrame(tls, unmask: false, cancellationToken);
        CollectionAssert.AreEqual(new byte[] { 1, (byte)RequestCode.Hello }, first.Payload[..2]);
        await WritePaddingTestResponse(tls, new HelloResponse {
            ErrorCode = SessionErrorCode.Ok, ProtocolVersion = 15, StartPadding = enabled
        }, cancellationToken);
        await using var result = await pending;
        Assert.IsFalse(connector.InitialPadding.OffersMasking(isTcpPacketChannel: false));
        connector.InitialPadding.Enable(result.Response.StartPadding);
        Assert.AreEqual(enabled, result.Response.StartPadding);
        Assert.IsFalse(((WebSocketStream)result.StreamConnection.Stream).UsesStandardMasking);
    }

    private static Task WritePaddingUpgrade(Stream stream, IReadOnlyDictionary<string, string> headers,
        bool responseEcho, CancellationToken cancellationToken)
    {
        var maskingHeader = responseEcho ? "X-WebSocketMasking: true\r\n" : "";
        return stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\nConnection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {WebSocketUtils.ComputeWebSocketAccept(headers["Sec-WebSocket-Key"])}\r\n" +
            maskingHeader + "\r\n"), cancellationToken).AsTask();
    }

    private static Task WritePaddingTestResponse(Stream stream, SessionResponse response,
        CancellationToken cancellationToken)
    {
        var body = StreamUtils.ObjectToJsonBuffer(response);
        var frame = new byte[body.Length + 14];
        var headerLength = WebSocketUtils.BuildWebSocketFrameHeader(frame, body.Length).Length;
        body.CopyTo(frame.AsMemory(headerLength));
        return stream.WriteAsync(frame.AsMemory(0, headerLength + body.Length), cancellationToken).AsTask();
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

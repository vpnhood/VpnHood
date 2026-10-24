using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Core.Tunneling.WebSockets;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.AccessManagers;

namespace VpnHood.Test.Tests;

[TestClass]
public class MaintenanceTest : TestBase
{
    // what goes on the wire: a web server's own answer, with no VpnHood session response after it
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Maintenance_answers_a_hello_as_a_web_server(bool useWebSocket, bool useQuic)
    {
        var accessManager = TestHttpAccessManager.Create(TestHelper.CreateAccessManager());
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;

        await using var connectorService = CreateConnectorService(server, token, useWebSocket, useQuic);
        var payload = BuildHelloPayload(token);
        await using var connection = await connectorService.GetConnectionToServer("maintenance-test", payload.Length,
            isTcpPacketChannel: false, onConnectAttempt: null, TestCt);
        await connection.Stream.WriteAsync(payload, TestCt);
        await connection.Stream.FlushAsync(TestCt);

        if (useWebSocket) {
            Assert.AreEqual(0, await connection.Stream.ReadAsync(new byte[1], TestCt));
            Assert.AreEqual(WebSocketUtils.CloseStatusTryAgainLater, ((WebSocketStream)connection.Stream).CloseStatus);
        }
        else {
            var response = await HttpUtils.ReadResponse(connection.Stream, TestCt);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.AreEqual(0, await connection.Stream.ReadAsync(new byte[1], TestCt));
        }
    }

    // no client sends it; the server still serves it, so a request is written by hand
    [TestMethod]
    public async Task Maintenance_answers_a_hello_with_a_503_on_a_WebSocket_with_no_handshake()
    {
        var accessManager = TestHttpAccessManager.Create(TestHelper.CreateAccessManager());
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(token.ServerToken.HostEndPoints!.First(), TestCt);
        await using var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = token.ServerToken.HostName,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }, TestCt);

        var payload = BuildHelloPayload(token);
        var header =
            "POST /maintenance-test HTTP/1.1\r\n" +
            $"Host: {token.ServerToken.HostName}\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "X-Buffered: false\r\n" +
            $"X-ProtocolVersion: {VpnHoodClientConfig.MaxProtocolVersion}\r\n" +
            $"X-BinaryStream: {TunnelStreamType.WebSocketNoHandshake}\r\n" +
            "\r\n";
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(header), TestCt);

        await using var webSocketStream = new WebSocketStream(sslStream, "maintenance-test", useBuffer: false,
            isServer: false);
        await webSocketStream.WriteAsync(payload, TestCt);
        await webSocketStream.FlushAsync(TestCt);

        var response = await HttpUtils.ReadResponse(webSocketStream, TestCt);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(0, await webSocketStream.ReadAsync(new byte[1], TestCt));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Maintenance_reaches_the_client(bool useWebSocket, bool useQuic)
    {
        var accessManager = TestHttpAccessManager.Create(TestHelper.CreateAccessManager());
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;

        await using var requestSender =
            new RequestSender(CreateConnectorService(server, token, useWebSocket, useQuic));
        await Assert.ThrowsExactlyAsync<MaintenanceException>(() =>
            requestSender.SendRequest<HelloResponse>(CreateHelloRequest(token), TestCt));
    }

    [TestMethod]
    public async Task Probe_Without_Valid_Request_Gets_401_In_Maintenance()
    {
        var accessManager = TestHttpAccessManager.Create(TestHelper.CreateAccessManager());
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;

        var handler = new HttpClientHandler {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var httpClient = new HttpClient(handler);
        var url = $"https://{token.ServerToken.HostEndPoints!.First()}";

        var ex = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => httpClient.GetStringAsync(url));
        Assert.AreEqual(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [TestMethod]
    [DataRow(502, true)]
    [DataRow(503, true)]
    [DataRow(504, true)]
    [DataRow(521, true)]
    [DataRow(500, false)]
    [DataRow(403, false)]
    public async Task HttpAccessManager_StatusCode_Handling(int statusCode, bool expectMaintenance)
    {
        using var accessManager = TestHttpAccessManager.Create(TestHelper.CreateAccessManager());
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = (HttpStatusCode)statusCode;

        if (expectMaintenance) {
            await Assert.ThrowsExactlyAsync<MaintenanceException>(() =>
                accessManager.Session_GetAll(TestCt));
        }
        else {
            var ex = await Assert.ThrowsAsync<Exception>(() =>
                accessManager.Session_GetAll(TestCt));
            Assert.IsNotInstanceOfType<MaintenanceException>(ex);
            Assert.IsInstanceOfType<ApiException>(ex);
        }
    }

    private ConnectorService CreateConnectorService(VpnHoodServer server, Token token, bool useWebSocket,
        bool useQuic)
    {
        if (useQuic && (!QuicListener.IsSupported || !QuicConnection.IsSupported))
            Assert.Inconclusive("QUIC is not supported on this platform.");

        var quicEndPoint = useQuic ? server.ServerHost.QuicEndPoints.FirstOrDefault() : null;
        if (useQuic && quicEndPoint == null)
            Assert.Inconclusive("No QUIC endpoint found on server.");

        var serverToken = token.ServerToken;
        var connectorService = new ConnectorService(new ConnectorServiceOptions {
            SocketFactory = TestHelper.CreateTestSocketFactory(),
            ProxyConnector = null,
            VpnEndPoint = new VpnEndPoint(serverToken.HostEndPoints!.First(), serverToken.HostName,
                serverToken.CertificateHash, serverToken.PathBase),
            RequestTimeout = TimeSpan.FromSeconds(10),
            AllowChannelReuse = false
        });

        connectorService.Init(
            protocolVersion: connectorService.ProtocolVersion,
            serverSecret: null,
            channelIdleTimeout: TimeSpan.FromSeconds(30),
            useWebSocket: useWebSocket,
            requestTimeout: TimeSpan.FromSeconds(10),
            useQuic: useQuic,
            quicEndPoint: quicEndPoint);

        return connectorService;
    }

    private static HelloRequest CreateHelloRequest(Token token)
    {
        var clientInfo = new ClientInfo {
            ClientId = Guid.NewGuid().ToString(),
            ClientVersion = "1.0.0",
            MinProtocolVersion = VpnHoodClientConfig.MinProtocolVersion,
            MaxProtocolVersion = VpnHoodClientConfig.MaxProtocolVersion,
            UserAgent = "Test"
        };

        return new HelloRequest {
            RequestId = UniqueIdFactory.Create(),
            EncryptedClientId = VhUtils.EncryptClientId(clientInfo.ClientId, token.Secret),
            ClientInfo = clientInfo,
            TokenId = token.TokenId
        };
    }

    // as the client writes a request: its version, its code, then its JSON
    private static byte[] BuildHelloPayload(Token token)
    {
        var request = CreateHelloRequest(token);
        var requestBuffer = StreamUtils.ObjectToJsonBuffer(request);
        var payload = new byte[2 + requestBuffer.Length];
        payload[0] = 1;
        payload[1] = request.RequestCode;
        requestBuffer.Span.CopyTo(payload.AsSpan(2));
        return payload;
    }
}

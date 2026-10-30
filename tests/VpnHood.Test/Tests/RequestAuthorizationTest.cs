using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using VpnHood.Core.Client;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.ProbeResistance;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class RequestAuthorizationTest : TestBase
{
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(true, false)]
    public async Task Post_and_upgrade_correct_the_clock_again_but_retry_each_request_once(bool useWebSocket, bool acceptRetry)
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var secret = RandomNumberGenerator.GetBytes(16);
        var signer = new ClientRequestSigner(secret, "localhost");
        await using var sender = new RequestSender(CreateConnector((IPEndPoint)listener.LocalEndpoint,
            certificate, signer, useWebSocket), signer);
        var request = new ServerCheckRequest { RequestId = "clock-correction" };
        var proof = new RequestProof(secret);
        var previousTime = DateTimeOffset.UtcNow;
        var clockCorrectionVersion = 0L;

        // Moving the peer's Date models a device-clock change without altering the machine's clock.
        foreach (var offset in new[] { TimeSpan.FromHours(2), TimeSpan.FromDays(-2) }) {
            var serverTime = DateTimeOffset.UtcNow + offset;
            var receivedTokens = new List<string>();
            var serverTask = ReplyToRequests(listener, certificate, serverTime, receivedTokens, useWebSocket, acceptRetry, TestCt);
            if (acceptRetry) {
                await using var result = await sender.SendRequest<SessionResponse>(request, TestCt);
                Assert.AreEqual(SessionErrorCode.Ok, result.Response.ErrorCode);
            }
            else {
                await Assert.ThrowsExactlyAsync<RequestAuthorizationException>(() =>
                    sender.SendRequest<SessionResponse>(request, TestCt));
            }

            await serverTask;
            Assert.HasCount(2, receivedTokens);
            Assert.IsTrue(proof.Verify(receivedTokens[0], "localhost", 15, previousTime));
            Assert.IsFalse(proof.Verify(receivedTokens[0], "localhost", 15, serverTime));
            Assert.IsTrue(proof.Verify(receivedTokens[1], "localhost", 15, serverTime));
            clockCorrectionVersion++;
            Assert.IsFalse(signer.TryCorrectClock(serverTime, clockCorrectionVersion));
            previousTime = serverTime;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Concurrent_requests_retry_after_another_request_corrects_the_clock(bool useWebSocket)
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var secret = RandomNumberGenerator.GetBytes(16);
        var signer = new ClientRequestSigner(secret, "localhost");
        await using var sender = new RequestSender(CreateConnector((IPEndPoint)listener.LocalEndpoint,
            certificate, signer, useWebSocket), signer);
        var originalTime = DateTimeOffset.UtcNow;
        var serverTime = originalTime.AddHours(2);
        var receivedTokens = new List<string>();
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadlineCts.CancelAfter(TimeSpan.FromSeconds(15));
        var firstRequest = SendSuccessfulRequest(sender, "concurrent-clock-first", deadlineCts.Token);
        var secondRequest = SendSuccessfulRequest(sender, "concurrent-clock-second", deadlineCts.Token);
        var serverTask = ReplyToConcurrentRequests(listener, certificate, serverTime, receivedTokens,
            useWebSocket, Task.WhenAny(firstRequest, secondRequest), deadlineCts.Token);

        await Task.WhenAll(firstRequest, secondRequest, serverTask);
        Assert.HasCount(4, receivedTokens);
        var proof = new RequestProof(secret);
        foreach (var token in receivedTokens.Take(2)) {
            Assert.IsTrue(proof.Verify(token, "localhost", 15, originalTime));
            Assert.IsFalse(proof.Verify(token, "localhost", 15, serverTime));
        }
        foreach (var token in receivedTokens.Skip(2))
            Assert.IsTrue(proof.Verify(token, "localhost", 15, serverTime));
        Assert.IsFalse(signer.TryCorrectClock(serverTime, 1));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Post_and_upgrade_select_the_proof_after_tls_connects(bool useWebSocket)
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var secret = RandomNumberGenerator.GetBytes(16);
        var signer = new ClientRequestSigner(secret, "localhost");
        await using var sender = new RequestSender(CreateConnector((IPEndPoint)listener.LocalEndpoint,
            certificate, signer, useWebSocket), signer);
        var originalTime = DateTimeOffset.UtcNow;
        var serverTime = originalTime.AddHours(2);
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadlineCts.CancelAfter(TimeSpan.FromSeconds(15));
        var requestTask = SendSuccessfulRequest(sender, "proof-after-tls", deadlineCts.Token);
        var serverTask = ReplyAfterClockChangeDuringTls(listener, certificate, signer, serverTime,
            useWebSocket, deadlineCts.Token);

        await Task.WhenAll(requestTask, serverTask);
        var receivedToken = await serverTask;
        var proof = new RequestProof(secret);
        Assert.IsTrue(proof.Verify(receivedToken, "localhost", 15, serverTime));
        Assert.IsFalse(proof.Verify(receivedToken, "localhost", 15, originalTime));
        Assert.AreEqual(1L, signer.ClockCorrectionVersion);
    }

    [TestMethod]
    public async Task Response_headers_keep_date_and_bearer_challenge_without_consuming_body()
    {
        const string date = "Wed, 02 Jan 2030 03:04:05 GMT";
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 401 Unauthorized\r\nDate: {date}\r\nWWW-Authenticate: Bearer\r\nContent-Length: 4\r\n\r\nbody"));
        using var response = await HttpUtils.ReadResponse(stream, TestCt);
        Assert.AreEqual(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), response.Headers.Date);
        Assert.AreEqual("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.AreEqual(4, response.Content.Headers.ContentLength);
        var body = new byte[4];
        await stream.ReadExactlyAsync(body, TestCt);
        CollectionAssert.AreEqual("body"u8.ToArray(), body);
    }

    [TestMethod]
    public async Task Discovery_keeps_each_farms_key_out_of_saved_endpoints()
    {
        var secret1 = RandomNumberGenerator.GetBytes(16);
        var secret2 = RandomNumberGenerator.GetBytes(16);
        var finder = new ServerFinder(new TestSocketFactory(), secret1, null, TimeSpan.FromSeconds(10),
            EndPointStrategy.IpOnly, [], null, null, false, null);
        var endpoints = await finder.ResolveVpnEndPoints([
            CreateServerToken("same.example.com", secret1),
            CreateServerToken("same.example.com", secret2)
        ], false, TestCt);

        Assert.AreEqual(endpoints[0], endpoints[1]);
        Assert.AreSame(secret1, finder.GetServerSecret(endpoints[0]));
        Assert.AreSame(secret2, finder.GetServerSecret(endpoints[1]));
        var saved = JsonSerializer.Serialize(endpoints);
        Assert.DoesNotContain(Convert.ToBase64String(secret1), saved);
        Assert.DoesNotContain(Convert.ToBase64String(secret2), saved);
        Assert.DoesNotContain("Secret", saved);
    }

    private static ServerToken CreateServerToken(string hostName, byte[] secret) => new() {
        CreatedTime = DateTime.UtcNow,
        HostName = hostName,
        HostPort = 443,
        IsValidHostName = false,
        Secret = secret,
        HostEndPoints = [new IPEndPoint(IPAddress.Loopback, 443)]
    };

    private static ConnectorService CreateConnector(IPEndPoint endPoint, X509Certificate2 certificate,
        ClientRequestSigner signer, bool useWebSocket)
    {
        var connector = new ConnectorService(new ConnectorServiceOptions {
            VpnEndPoint = new VpnEndPoint(endPoint, "localhost", certificate.GetCertHash(), null),
            ProxyConnector = null,
            SocketFactory = new TestSocketFactory(),
            RequestTimeout = TimeSpan.FromSeconds(10),
            AllowChannelReuse = false
        }, signer);
        connector.Init(TimeSpan.FromSeconds(30), useWebSocket, TimeSpan.FromSeconds(10), false, null);
        return connector;
    }

    private static async Task SendSuccessfulRequest(RequestSender sender, string requestId,
        CancellationToken cancellationToken)
    {
        await using var result = await sender.SendRequest<SessionResponse>(
            new ServerCheckRequest { RequestId = requestId }, cancellationToken);
        Assert.AreEqual(SessionErrorCode.Ok, result.Response.ErrorCode);
    }

    private static async Task ReplyToRequests(TcpListener listener, X509Certificate2 certificate,
        DateTimeOffset serverTime, List<string> tokens, bool useWebSocket, bool acceptRetry,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++) {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var tls = new SslStream(client.GetStream());
            var headers = await ReadRequest(tls, certificate, useWebSocket, cancellationToken);
            tokens.Add(headers["Authorization"][7..]);

            if (attempt == 0 || !acceptRetry) {
                // Even another meaningful Date on the retry must not trigger a third attempt.
                var responseTime = attempt == 0 ? serverTime : serverTime.AddHours(1);
                await ReplyUnauthorized(tls, responseTime, cancellationToken);
                continue;
            }

            await ReplySuccess(tls, headers, useWebSocket, cancellationToken);
        }
    }

    private static async Task ReplyToConcurrentRequests(TcpListener listener, X509Certificate2 certificate,
        DateTimeOffset serverTime, List<string> tokens, bool useWebSocket, Task firstRequestCompletion,
        CancellationToken cancellationToken)
    {
        using var firstClient = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var firstTls = new SslStream(firstClient.GetStream());
        var firstHeaders = await ReadRequest(firstTls, certificate, useWebSocket, cancellationToken);
        tokens.Add(firstHeaders["Authorization"][7..]);
        using var secondClient = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var secondTls = new SslStream(secondClient.GetStream());
        var secondHeaders = await ReadRequest(secondTls, certificate, useWebSocket, cancellationToken);
        tokens.Add(secondHeaders["Authorization"][7..]);

        // Both original proofs are on the wire before either rejection can correct the shared clock.
        await ReplyUnauthorized(firstTls, serverTime, cancellationToken);
        using var firstRetryClient = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var firstRetryTls = new SslStream(firstRetryClient.GetStream());
        var firstRetryHeaders = await ReadRequest(firstRetryTls, certificate, useWebSocket, cancellationToken);
        tokens.Add(firstRetryHeaders["Authorization"][7..]);
        await ReplySuccess(firstRetryTls, firstRetryHeaders, useWebSocket, cancellationToken);
        await firstRequestCompletion.WaitAsync(cancellationToken);

        // This rejection belongs to the older proof, after another request has already corrected it.
        await ReplyUnauthorized(secondTls, serverTime, cancellationToken);
        using var secondRetryClient = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var secondRetryTls = new SslStream(secondRetryClient.GetStream());
        var secondRetryHeaders = await ReadRequest(secondRetryTls, certificate, useWebSocket, cancellationToken);
        tokens.Add(secondRetryHeaders["Authorization"][7..]);
        await ReplySuccess(secondRetryTls, secondRetryHeaders, useWebSocket, cancellationToken);
    }

    private static async Task<string> ReplyAfterClockChangeDuringTls(TcpListener listener,
        X509Certificate2 certificate, ClientRequestSigner signer, DateTimeOffset serverTime,
        bool useWebSocket, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        // Peek leaves ClientHello intact while proving the client is waiting for TLS authentication.
        Assert.AreEqual(1, await client.Client.ReceiveAsync(new byte[1], SocketFlags.Peek, cancellationToken));
        Assert.IsTrue(signer.TryCorrectClock(serverTime, 0));
        await using var tls = new SslStream(client.GetStream());
        var headers = await ReadRequest(tls, certificate, useWebSocket, cancellationToken);
        await ReplySuccess(tls, headers, useWebSocket, cancellationToken);
        return headers["Authorization"][7..];
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadRequest(SslStream tls,
        X509Certificate2 certificate, bool useWebSocket, CancellationToken cancellationToken)
    {
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
            ServerCertificate = certificate,
            ApplicationProtocols = [SslApplicationProtocol.Http11]
        }, cancellationToken);
        var headers = await HttpUtils.ParseHeadersAsync(tls, cancellationToken);
        Assert.IsNotNull(headers);
        Assert.AreEqual("15", headers["X-ProtocolVersion"]);
        StringAssert.StartsWith(headers["Authorization"], "Bearer ");
        if (!useWebSocket) {
            var body = new byte[int.Parse(headers["Content-Length"])];
            await tls.ReadExactlyAsync(body, cancellationToken);
        }
        return headers;
    }

    private static async Task ReplyUnauthorized(SslStream tls, DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {
        await tls.WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 401 Unauthorized\r\nDate: {serverTime:R}\r\nWWW-Authenticate: Bearer\r\nContent-Length: 0\r\n\r\n"), cancellationToken);
    }

    private static async Task ReplySuccess(SslStream tls, IReadOnlyDictionary<string, string> headers,
        bool useWebSocket, CancellationToken cancellationToken)
    {
        if (useWebSocket) {
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(
                headers["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await tls.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), cancellationToken);
            await using var webSocket = new WebSocketStream(tls, "clock-test", true, isServer: true);
            await webSocket.ReadExactlyAsync(new byte[2], cancellationToken);
            await StreamUtils.ReadMessageAsync(webSocket, cancellationToken);
            await StreamUtils.WriteObjectAsync(webSocket, new SessionResponse { ErrorCode = SessionErrorCode.Ok }, cancellationToken);
            await webSocket.FlushAsync(cancellationToken);
        }
        else {
            var body = StreamUtils.ObjectToJsonBuffer(new SessionResponse { ErrorCode = SessionErrorCode.Ok });
            await tls.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n\r\n"), cancellationToken);
            await tls.WriteAsync(body, cancellationToken);
        }
    }
}

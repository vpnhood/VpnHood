using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using VpnHood.Core.Client;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.ProbeResistance;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class QuicRequestRecoveryTest : TestBase
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_later_clock_change_recovers_on_the_next_fresh_quic_request(bool useWebSocket)
    {
        RequireQuic();
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var sockets = new CountingSocketFactory();
        var signer = CreateSigner(token);
        await using var sender = new RequestSender(CreateConnector(server, token, signer, sockets, useWebSocket), signer);
        var session = await CreateSession(sender, token, TestCt);
        sender.ConnectorService.UseQuic = true;
        await SendStatus(sender, session, TestCt);
        Assert.AreEqual(1, sockets.TcpCount, "Healthy QUIC requests must not add TLS clock probes.");
        var streamsBeforeFailure = sockets.QuicStreamCount;

        // Move this signer's clock after Hello and a successful QUIC request, without changing the host clock.
        Assert.IsTrue(signer.TryCorrectClock(DateTimeOffset.UtcNow.AddHours(1), 0));
        sender.ConnectorService.RequestTimeout = TimeSpan.FromSeconds(1);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => SendStatus(sender, session, TestCt));
        Assert.AreEqual(1, sockets.TcpCount, "The failed request must not start a recovery or replay itself.");
        Assert.AreEqual(streamsBeforeFailure + 1, sockets.QuicStreamCount,
            "A failed request must not be replayed on another QUIC stream.");

        sender.ConnectorService.RequestTimeout = TimeSpan.FromSeconds(10);
        await SendStatus(sender, session, TestCt);
        Assert.AreEqual(2, sockets.TcpCount, "The next fresh request must obtain one verified TLS Date.");
        Assert.AreEqual(2L, signer.ClockCorrectionVersion);
        Assert.IsTrue(sender.ConnectorService.UseQuic);
        Assert.HasCount(1, server.SessionManager.Sessions, "Recovery must not create a new Hello session.");
        await SendStatus(sender, session, TestCt);
        Assert.AreEqual(2, sockets.TcpCount, "Successful recovery must clear the pending probe.");
    }

    [TestMethod]
    public async Task Concurrent_recovery_waiters_share_one_plain_tls_probe()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var sockets = new CountingSocketFactory();
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16), "localhost");
        var endPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost", certificate.GetCertHash(), "/base");
        using var tcpFactory = CreateTcpFactory(sockets, endPoint, certificate);
        var recovery = new QuicRequestRecovery(tcpFactory, signer, endPoint);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await recovery.Recover("healthy", deadline.Token);
        Assert.AreEqual(0, sockets.TcpCount);
        for (var i = 0; i < 4; i++)
            recovery.RequireRecovery();

        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTime = DateTimeOffset.UtcNow.AddHours(2);
        var peer = ReplyToClockProbe(listener, certificate, serverTime, received, release.Task, deadline.Token);
        var requests = Enumerable.Range(0, 4)
            .Select(index => recovery.Recover($"concurrent-{index}", deadline.Token)).ToArray();
        await received.Task.WaitAsync(deadline.Token);
        Assert.AreEqual(1, sockets.TcpCount);
        release.SetResult();
        await Task.WhenAll(requests.Append(peer));
        Assert.AreEqual(1, sockets.TcpCount);
        Assert.AreEqual(1L, signer.ClockCorrectionVersion);
        await recovery.Recover("after-success", deadline.Token);
        Assert.AreEqual(1, sockets.TcpCount);
    }

    [TestMethod]
    public async Task Cancellation_bounds_the_probe_and_its_waiters_without_losing_pending_recovery()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var sockets = new CountingSocketFactory();
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16), "localhost");
        var endPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost", certificate.GetCertHash(), "/base");
        using var tcpFactory = CreateTcpFactory(sockets, endPoint, certificate);
        var recovery = new QuicRequestRecovery(tcpFactory, signer, endPoint);
        recovery.RequireRecovery();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var waiterCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = WithholdClockResponse(listener, certificate, received, deadline.Token);
        var first = recovery.Recover("cancel-active", probeCancellation.Token);
        await received.Task.WaitAsync(deadline.Token);
        var waiter = recovery.Recover("cancel-waiter", waiterCancellation.Token);
        await waiterCancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiter.WaitAsync(TimeSpan.FromSeconds(2), deadline.Token));
        Assert.AreEqual(1, sockets.TcpCount, "A canceled waiter must not start another probe.");
        await probeCancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(2), deadline.Token));
        await peer;
        Assert.AreEqual(0L, signer.ClockCorrectionVersion);

        // Canceling the in-flight probe must not consume the recovery requirement.
        var retryPeer = ReplyToClockProbe(listener, certificate, DateTimeOffset.UtcNow.AddHours(2),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), Task.CompletedTask, deadline.Token);
        await recovery.Recover("after-cancellation", deadline.Token);
        await retryPeer;
        Assert.AreEqual(2, sockets.TcpCount);
        Assert.AreEqual(1L, signer.ClockCorrectionVersion);
    }

    [TestMethod]
    public async Task A_wrong_farm_key_is_not_fixed_by_switching_application_requests_to_tcp()
    {
        RequireQuic();
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var sockets = new CountingSocketFactory();
        var goodSigner = CreateSigner(token);
        await using var goodSender = new RequestSender(CreateConnector(server, token, goodSigner, sockets, false), goodSigner);
        var session = await CreateSession(goodSender, token, TestCt);
        var wrongSigner = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16), token.ServerToken.HostName);
        await using var sender = new RequestSender(CreateConnector(server, token, wrongSigner, sockets, false), wrongSigner);
        sender.ConnectorService.UseQuic = true;
        sender.ConnectorService.RequestTimeout = TimeSpan.FromSeconds(1);

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => SendStatus(sender, session, TestCt));
        Assert.AreEqual(1, sockets.TcpCount);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => SendStatus(sender, session, TestCt));
        Assert.AreEqual(2, sockets.TcpCount, "Only the clock probe may use TCP after the failed QUIC request.");
        Assert.AreEqual(2, sockets.QuicStreamCount, "Each application request gets one QUIC attempt.");
        Assert.IsTrue(sender.ConnectorService.UseQuic);
        Assert.AreEqual(0L, wrongSigner.ClockCorrectionVersion, "An accurate clock cannot repair a wrong farm key.");
        Assert.HasCount(1, server.SessionManager.Sessions);
    }

    private static void RequireQuic()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            Assert.Inconclusive("QUIC is not supported on this platform.");
    }

    private static ClientRequestSigner CreateSigner(Token token) => new(
        token.ServerToken.Secret ?? throw new InvalidOperationException("The token has no farm secret."),
        token.ServerToken.HostName);

    private static ConnectorService CreateConnector(VpnHoodServer server, Token token, ClientRequestSigner signer,
        CountingSocketFactory sockets, bool useWebSocket)
    {
        var serverToken = token.ServerToken;
        var connector = new ConnectorService(new ConnectorServiceOptions {
            VpnEndPoint = new VpnEndPoint(serverToken.HostEndPoints!.First(), serverToken.HostName,
                serverToken.CertificateHash, serverToken.PathBase),
            ProxyConnector = null,
            SocketFactory = sockets,
            RequestTimeout = TimeSpan.FromSeconds(10),
            AllowChannelReuse = false
        }, signer);
        connector.Init(TimeSpan.FromSeconds(30), useWebSocket, TimeSpan.FromSeconds(10), false,
            server.ServerHost.QuicEndPoints.Single());
        return connector;
    }

    private static async Task<HelloResponse> CreateSession(RequestSender sender, Token token,
        CancellationToken cancellationToken)
    {
        var info = new ClientInfo {
            ClientId = Guid.NewGuid().ToString(),
            ClientVersion = "1.0.0",
            MinProtocolVersion = VpnHoodClientConfig.MinProtocolVersion,
            MaxProtocolVersion = VpnHoodClientConfig.MaxProtocolVersion,
            UserAgent = "QuicRequestRecoveryTest"
        };
        await using var result = await sender.SendRequest<HelloResponse>(new HelloRequest {
            RequestId = Guid.NewGuid().ToString(),
            ClientInfo = info,
            EncryptedClientId = VhUtils.EncryptClientId(info.ClientId, token.Secret),
            TokenId = token.TokenId
        }, cancellationToken);
        Assert.AreEqual(SessionErrorCode.Ok, result.Response.ErrorCode);
        Assert.IsNotNull(result.Response.QuicPort);
        return result.Response;
    }

    private static async Task SendStatus(RequestSender sender, HelloResponse session, CancellationToken cancellationToken)
    {
        await using var result = await sender.SendRequest<SessionResponse>(new SessionStatusRequest {
            RequestId = Guid.NewGuid().ToString(),
            SessionId = session.SessionId,
            SessionKey = session.SessionKey
        }, cancellationToken);
        Assert.AreEqual(SessionErrorCode.Ok, result.Response.ErrorCode);
    }

    private static TcpStreamConnectionFactory CreateTcpFactory(CountingSocketFactory sockets, VpnEndPoint endPoint,
        X509Certificate2 certificate) => new(sockets, null, endPoint,
        (_, presented, _, _) => presented?.GetCertHashString() == certificate.GetCertHashString(), null);

    private static async Task ReadClockProbe(SslStream tls, X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
            ServerCertificate = certificate,
            ApplicationProtocols = [SslApplicationProtocol.Http11]
        }, cancellationToken);
        var headers = await HttpUtils.ParseHeadersAsync(tls, cancellationToken);
        Assert.IsNotNull(headers);
        StringAssert.StartsWith(headers[HttpUtils.HttpRequestKey], "GET /base/");
        Assert.AreEqual("localhost", headers["Host"]);
        Assert.AreEqual("close", headers["Connection"]);
        Assert.IsFalse(headers.ContainsKey("Authorization"));
        Assert.IsFalse(headers.ContainsKey("Content-Length"));
        Assert.IsFalse(headers.ContainsKey("X-ProtocolVersion"));
    }

    private static async Task ReplyToClockProbe(TcpListener listener, X509Certificate2 certificate,
        DateTimeOffset serverTime, TaskCompletionSource received, Task release, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var tls = new SslStream(client.GetStream());
        await ReadClockProbe(tls, certificate, cancellationToken);
        received.SetResult();
        await release.WaitAsync(cancellationToken);
        await tls.WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 401 Unauthorized\r\nDate: {serverTime:R}\r\nWWW-Authenticate: Bearer\r\n" +
            "Content-Length: 0\r\nConnection: close\r\n\r\n"), cancellationToken);
    }

    private static async Task WithholdClockResponse(TcpListener listener, X509Certificate2 certificate,
        TaskCompletionSource received, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var tls = new SslStream(client.GetStream());
        await ReadClockProbe(tls, certificate, cancellationToken);
        received.SetResult();
        try {
            Assert.AreEqual(0, await tls.ReadAsync(new byte[1], cancellationToken),
                "The clock probe must contain no application payload.");
        }
        catch (IOException) {
            // Cancellation may abort TLS instead of sending close_notify. Both release the held connection.
        }
    }

    private sealed class CountingSocketFactory : TestSocketFactory
    {
        private int _tcpCount;
        private int _quicStreamCount;
        public int TcpCount => Volatile.Read(ref _tcpCount);
        public int QuicStreamCount => Volatile.Read(ref _quicStreamCount);

        public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
        {
            Interlocked.Increment(ref _tcpCount);
            return base.CreateTcpClient(ipEndPoint, options);
        }

        public override IQuicClient CreateQuicClient() => new CountingQuicClient(base.CreateQuicClient(), this);

        private sealed class CountingQuicClient(IQuicClient client, CountingSocketFactory owner) : IQuicClient
        {
            public async ValueTask<IQuicConnection> ConnectAsync(QuicClientConnectOptions options,
                CancellationToken cancellationToken) =>
                new CountingQuicConnection(await client.ConnectAsync(options, cancellationToken), owner);
        }

        private sealed class CountingQuicConnection(IQuicConnection connection, CountingSocketFactory owner)
            : IQuicConnection
        {
            public IPEndPoint LocalEndPoint => connection.LocalEndPoint;
            public IPEndPoint RemoteEndPoint => connection.RemoteEndPoint;

            public ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref owner._quicStreamCount);
                return connection.OpenOutboundStreamAsync(cancellationToken);
            }

            public ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken) =>
                connection.AcceptInboundStreamAsync(cancellationToken);

            public ValueTask DisposeAsync() => connection.DisposeAsync();
        }
    }
}

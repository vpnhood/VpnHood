using System.Diagnostics;
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
        var sockets = new QuicRecoverySocketFactory();
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
    public async Task Concurrent_requests_continue_while_one_plain_tls_probe_is_pending()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var sockets = new QuicRecoverySocketFactory();
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16), "localhost");
        var endPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost", certificate.GetCertHash(), "/base");
        using var tcpFactory = CreateTcpFactory(sockets, endPoint, certificate);
        var recovery = new QuicRequestRecovery(tcpFactory, signer, endPoint, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await recovery.Recover("healthy", deadline.Token);
        Assert.AreEqual(0, sockets.TcpCount);
        for (var i = 0; i < 4; i++)
            recovery.RequireRecovery();

        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTime = DateTimeOffset.UtcNow.AddHours(2);
        var peer = ReplyToClockProbe(listener, certificate, endPoint, serverTime, received, release.Task, deadline.Token);
        var first = recovery.Recover("first", deadline.Token);
        await received.Task.WaitAsync(deadline.Token);
        try {
            var requests = Enumerable.Range(0, 4)
                .Select(index => recovery.Recover($"concurrent-{index}", deadline.Token)).ToArray();
            Assert.IsTrue(requests.All(request => request.IsCompletedSuccessfully),
                "Other requests must continue without waiting for the active clock probe.");
            await Task.WhenAll(requests);
            Assert.IsFalse(first.IsCompleted, "The probe must remain held until its response is released.");
            Assert.AreEqual(1, sockets.TcpCount);
        }
        finally {
            release.TrySetResult();
            await Task.WhenAll(first, peer);
        }
        Assert.AreEqual(1, sockets.TcpCount);
        Assert.AreEqual(1L, signer.ClockCorrectionVersion);
        await recovery.Recover("after-success", deadline.Token);
        Assert.AreEqual(1, sockets.TcpCount);
    }

    [TestMethod]
    public async Task A_failure_during_a_successful_probe_keeps_a_later_probe_pending()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var sockets = new QuicRecoverySocketFactory();
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16), "localhost");
        var endPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost", certificate.GetCertHash(), "/base");
        using var tcpFactory = CreateTcpFactory(sockets, endPoint, certificate);
        var recovery = new QuicRequestRecovery(tcpFactory, signer, endPoint, TimeProvider.System);
        recovery.RequireRecovery();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTime = DateTimeOffset.UtcNow.AddHours(2);
        var peer = ReplyToClockProbe(listener, certificate, endPoint, serverTime,
            received, release.Task, deadline.Token);
        var first = recovery.Recover("before-new-failure", deadline.Token);
        await received.Task.WaitAsync(deadline.Token);
        recovery.RequireRecovery();
        release.SetResult();
        await Task.WhenAll(first, peer);

        var retryPeer = ReplyToClockProbe(listener, certificate, endPoint, serverTime,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), Task.CompletedTask, deadline.Token);
        await recovery.Recover("after-new-failure", deadline.Token);
        await retryPeer;
        Assert.AreEqual(2, sockets.TcpCount, "A successful probe must not consume failures raised while it ran.");
        await recovery.Recover("after-second-success", deadline.Token);
        Assert.AreEqual(2, sockets.TcpCount);
    }

    [TestMethod]
    public async Task Caller_cancellation_stops_the_probe_without_losing_pending_recovery()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var sockets = new QuicRecoverySocketFactory();
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16), "localhost");
        var endPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost", certificate.GetCertHash(), "/base");
        using var tcpFactory = CreateTcpFactory(sockets, endPoint, certificate);
        var recovery = new QuicRequestRecovery(tcpFactory, signer, endPoint, TimeProvider.System);
        recovery.RequireRecovery();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var otherCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = WithholdClockResponse(listener, certificate, endPoint, received, deadline.Token);
        var first = recovery.Recover("cancel-active", probeCancellation.Token);
        await received.Task.WaitAsync(deadline.Token);
        await otherCancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            recovery.Recover("already-canceled", otherCancellation.Token));
        Assert.AreEqual(1, sockets.TcpCount, "A canceled request must not start another probe.");
        await probeCancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(2), deadline.Token));
        await peer;
        Assert.AreEqual(0L, signer.ClockCorrectionVersion);

        // Canceling the in-flight probe must not consume the recovery requirement.
        var retryPeer = ReplyToClockProbe(listener, certificate, endPoint, DateTimeOffset.UtcNow.AddHours(2),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), Task.CompletedTask, deadline.Token);
        await recovery.Recover("after-cancellation", deadline.Token);
        await retryPeer;
        Assert.AreEqual(2, sockets.TcpCount);
        Assert.AreEqual(1L, signer.ClockCorrectionVersion);
    }

    [TestMethod]
    public async Task The_probes_own_deadline_leaves_recovery_pending_without_canceling_the_request()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var sockets = new QuicRecoverySocketFactory();
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16), "localhost");
        var endPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost", certificate.GetCertHash(), "/base");
        using var tcpFactory = CreateTcpFactory(sockets, endPoint, certificate);
        var timeProvider = new QuicRecoveryTimeProvider();
        var recovery = new QuicRequestRecovery(tcpFactory, signer, endPoint, timeProvider);
        recovery.RequireRecovery();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = WithholdClockResponse(listener, certificate, endPoint, received, deadline.Token);
        var probe = recovery.Recover("probe-timeout", deadline.Token);
        await received.Task.WaitAsync(deadline.Token);
        await probe.WaitAsync(TimeSpan.FromSeconds(5), deadline.Token);
        await peer.WaitAsync(deadline.Token);
        Assert.IsFalse(deadline.IsCancellationRequested);
        Assert.AreEqual(0L, signer.ClockCorrectionVersion);

        await recovery.Recover("during-timeout-cooldown", deadline.Token);
        Assert.AreEqual(1, sockets.TcpCount, "A timed-out probe must not delay the next request too.");
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var retryPeer = ReplyToClockProbe(listener, certificate, endPoint, DateTimeOffset.UtcNow.AddHours(2),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), Task.CompletedTask, deadline.Token);
        await recovery.Recover("after-probe-timeout", deadline.Token);
        await retryPeer;
        Assert.AreEqual(2, sockets.TcpCount);
        Assert.AreEqual(1L, signer.ClockCorrectionVersion);
    }

    [TestMethod]
    public async Task Failed_clock_probes_wait_a_minute_before_retrying_pending_recovery()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var sockets = new QuicRecoverySocketFactory { RejectTcpConnections = true };
        var signer = new ClientRequestSigner(RandomNumberGenerator.GetBytes(16), "localhost");
        var endPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost", certificate.GetCertHash(), "/base");
        using var tcpFactory = CreateTcpFactory(sockets, endPoint, certificate);
        var timeProvider = new QuicRecoveryTimeProvider();
        var recovery = new QuicRequestRecovery(tcpFactory, signer, endPoint, timeProvider);
        recovery.RequireRecovery();
        await recovery.Recover("first-failed-probe", TestCt);
        Assert.AreEqual(1, sockets.TcpCount);

        // More QUIC failures must not bypass a cooldown on an unavailable TCP path.
        recovery.RequireRecovery();
        timeProvider.Advance(TimeSpan.FromSeconds(59));
        var requests = Enumerable.Range(0, 4)
            .Select(index => recovery.Recover($"cooldown-{index}", TestCt)).ToArray();
        Assert.IsTrue(requests.All(request => request.IsCompletedSuccessfully));
        await Task.WhenAll(requests);
        Assert.AreEqual(1, sockets.TcpCount);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => recovery.Recover("canceled-cooldown", canceled.Token));

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await recovery.Recover("second-failed-probe", TestCt);
        Assert.AreEqual(2, sockets.TcpCount, "Pending recovery becomes eligible after one minute.");
        sockets.RejectTcpConnections = false;
        timeProvider.Advance(TimeSpan.FromSeconds(59));
        await recovery.Recover("second-cooldown", TestCt);
        Assert.AreEqual(2, sockets.TcpCount, "Each failed attempt starts its own cooldown.");

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var serverTime = DateTimeOffset.UtcNow.AddHours(2);
        var peer = ReplyToClockProbe(listener, certificate, endPoint, serverTime,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), Task.CompletedTask, TestCt);
        await recovery.Recover("successful-retry", TestCt);
        await peer;
        Assert.AreEqual(3, sockets.TcpCount);
        Assert.AreEqual(1L, signer.ClockCorrectionVersion);
        await recovery.Recover("after-success", TestCt);
        Assert.AreEqual(3, sockets.TcpCount, "Success clears pending recovery.");

        recovery.RequireRecovery();
        var nextPeer = ReplyToClockProbe(listener, certificate, endPoint, serverTime,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), Task.CompletedTask, TestCt);
        await recovery.Recover("new-failure-after-success", TestCt);
        await nextPeer;
        Assert.AreEqual(4, sockets.TcpCount, "A new failure after success can probe immediately.");
        await recovery.Recover("after-unchanged-clock-success", TestCt);
        Assert.AreEqual(4, sockets.TcpCount, "A verified Date clears pending recovery even without a clock change.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_failed_quic_connect_or_stream_open_does_not_schedule_a_clock_probe(bool failStreamOpen)
    {
        RequireQuic();
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var sockets = new QuicRecoverySocketFactory();
        var signer = CreateSigner(token);
        await using var sender = new RequestSender(CreateConnector(server, token, signer, sockets, false), signer);
        var session = await CreateSession(sender, token, TestCt);
        sender.ConnectorService.UseQuic = true;
        if (failStreamOpen)
            await SendStatus(sender, session, TestCt);
        sockets.RejectQuicConnects = !failStreamOpen;
        sockets.RejectQuicStreamOpens = failStreamOpen;

        await Assert.ThrowsExactlyAsync<IOException>(() => SendStatus(sender, session, TestCt));
        Assert.AreEqual(1, sockets.TcpCount);
        sockets.RejectQuicConnects = false;
        sockets.RejectQuicStreamOpens = false;
        await SendStatus(sender, session, TestCt);
        Assert.AreEqual(1, sockets.TcpCount,
            "A failure before an established request cannot establish a need for clock recovery.");
        Assert.AreEqual(0L, signer.ClockCorrectionVersion);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_failed_tcp_probe_does_not_prevent_quic_requests_during_its_cooldown(bool useWebSocket)
    {
        RequireQuic();
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var sockets = new QuicRecoverySocketFactory();
        var signer = CreateSigner(token);
        await using var sender = new RequestSender(CreateConnector(server, token, signer, sockets, useWebSocket), signer);
        var session = await CreateSession(sender, token, TestCt);
        sender.ConnectorService.UseQuic = true;
        Assert.IsTrue(signer.TryCorrectClock(DateTimeOffset.UtcNow.AddHours(1), signer.ClockCorrectionVersion));
        sender.ConnectorService.RequestTimeout = TimeSpan.FromSeconds(1);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => SendStatus(sender, session, TestCt));
        Assert.AreEqual(1, sockets.TcpCount);
        Assert.AreEqual(1, sockets.QuicStreamCount);

        // Restore the request's proof while TCP stays unavailable; only the application uses QUIC.
        Assert.IsTrue(signer.TryCorrectClock(DateTimeOffset.UtcNow, signer.ClockCorrectionVersion));
        sender.ConnectorService.RequestTimeout = TimeSpan.FromSeconds(10);
        sockets.RejectTcpConnections = true;
        await SendStatus(sender, session, TestCt);
        Assert.AreEqual(2, sockets.TcpCount);
        Assert.AreEqual(2, sockets.QuicStreamCount);
        await SendStatus(sender, session, TestCt);
        Assert.AreEqual(2, sockets.TcpCount, "A failed probe must not delay each following QUIC request.");
        Assert.AreEqual(3, sockets.QuicStreamCount);

        sockets.RejectTcpConnections = false;
        await SendStatus(sender, session, TestCt);
        Assert.AreEqual(2, sockets.TcpCount);
        await SendStatus(sender, session, TestCt);
        Assert.AreEqual(2, sockets.TcpCount, "QUIC requests must keep progressing throughout the cooldown.");
        Assert.AreEqual(5, sockets.QuicStreamCount);
        Assert.IsTrue(sender.ConnectorService.UseQuic);
        Assert.HasCount(1, server.SessionManager.Sessions);
    }

    [TestMethod]
    public async Task A_held_tcp_probe_leaves_the_quic_request_its_full_timeout_and_other_requests_progress()
    {
        RequireQuic();
        using var accessManager = await TestHelper.CreateAccessManager(TestCt);
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        var sockets = new QuicRecoverySocketFactory();
        var signer = CreateSigner(token);
        await using var sessionSender = new RequestSender(CreateConnector(server, token, signer, sockets, false), signer);
        var session = await CreateSession(sessionSender, token, TestCt);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, token.ServerToken.HostName,
            token.ServerToken.CertificateHash, token.ServerToken.PathBase);
        await using var sender = new RequestSender(CreateConnector(server, signer, sockets, false, endPoint), signer);
        sender.ConnectorService.UseQuic = true;
        await SendStatus(sender, session, TestCt);
        Assert.IsTrue(signer.TryCorrectClock(DateTimeOffset.UtcNow.AddHours(1), signer.ClockCorrectionVersion));
        sender.ConnectorService.RequestTimeout = TimeSpan.FromSeconds(1);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => SendStatus(sender, session, TestCt));
        Assert.IsTrue(signer.TryCorrectClock(DateTimeOffset.UtcNow, signer.ClockCorrectionVersion));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = WithholdClockResponse(listener, accessManager.DefaultCert, endPoint, received, deadline.Token);
        var elapsed = Stopwatch.StartNew();
        var first = SendStatus(sender, session, deadline.Token);
        await received.Task.WaitAsync(deadline.Token);
        await SendStatus(sender, session, deadline.Token);
        Assert.IsFalse(first.IsCompleted, "Another QUIC request must finish while the clock probe is still held.");
        await first.WaitAsync(TimeSpan.FromSeconds(5), deadline.Token);
        await peer;
        Assert.IsTrue(elapsed.Elapsed > sender.ConnectorService.RequestTimeout,
            "The probe must outlast the short application timeout without consuming it.");
        Assert.AreEqual(2, sockets.TcpCount, "Concurrent application requests must share the one active probe.");
        Assert.AreEqual(4, sockets.QuicStreamCount);
        await SendStatus(sender, session, deadline.Token);
        Assert.AreEqual(2, sockets.TcpCount, "A timed-out probe must not start again for the next request.");
        Assert.AreEqual(5, sockets.QuicStreamCount);
    }

    [TestMethod]
    public async Task Caller_cancellation_of_an_established_request_does_not_schedule_recovery()
    {
        RequireQuic();
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var sockets = new QuicRecoverySocketFactory();
        var signer = CreateSigner(token);
        await using var sender = new RequestSender(CreateConnector(server, token, signer, sockets, false), signer);
        var session = await CreateSession(sender, token, TestCt);
        sender.ConnectorService.UseQuic = true;
        await SendStatus(sender, session, TestCt);
        Assert.IsTrue(signer.TryCorrectClock(DateTimeOffset.UtcNow.AddHours(1), signer.ClockCorrectionVersion));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var request = SendStatus(sender, session, cancellation.Token);
        await AssertEqualsWait(2, () => sockets.QuicStreamCount);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request);

        Assert.IsTrue(signer.TryCorrectClock(DateTimeOffset.UtcNow, signer.ClockCorrectionVersion));
        await SendStatus(sender, session, TestCt);
        Assert.AreEqual(1, sockets.TcpCount, "Caller cancellation is not a failed proof and must not request a TLS probe.");
        Assert.AreEqual(3, sockets.QuicStreamCount);
    }

    [TestMethod]
    public async Task A_wrong_farm_key_is_not_fixed_by_switching_application_requests_to_tcp()
    {
        RequireQuic();
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var sockets = new QuicRecoverySocketFactory();
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
        QuicRecoverySocketFactory sockets, bool useWebSocket)
    {
        var serverToken = token.ServerToken;
        var hostEndPoints = serverToken.HostEndPoints ??
            throw new InvalidOperationException("The token has no host endpoints.");
        return CreateConnector(server, signer, sockets, useWebSocket,
            new VpnEndPoint(hostEndPoints.First(), serverToken.HostName, serverToken.CertificateHash, serverToken.PathBase));
    }

    private static ConnectorService CreateConnector(VpnHoodServer server, ClientRequestSigner signer,
        QuicRecoverySocketFactory sockets, bool useWebSocket, VpnEndPoint endPoint)
    {
        var connector = new ConnectorService(new ConnectorServiceOptions {
            VpnEndPoint = endPoint,
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

    private static TcpStreamConnectionFactory CreateTcpFactory(QuicRecoverySocketFactory sockets, VpnEndPoint endPoint,
        X509Certificate2 certificate) => new(sockets, null, endPoint,
        (_, presented, _, _) => presented?.GetCertHashString() == certificate.GetCertHashString(), null);

    private static async Task ReadClockProbe(SslStream tls, X509Certificate2 certificate,
        VpnEndPoint endPoint, CancellationToken cancellationToken)
    {
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
            ServerCertificate = certificate,
            ApplicationProtocols = [SslApplicationProtocol.Http11]
        }, cancellationToken);
        var headers = await HttpUtils.ParseHeadersAsync(tls, cancellationToken);
        Assert.IsNotNull(headers);
        var pathBase = endPoint.PathBase?.Trim('/');
        var path = string.IsNullOrEmpty(pathBase) ? "/" : $"/{pathBase}/";
        StringAssert.StartsWith(headers[HttpUtils.HttpRequestKey], $"GET {path}");
        Assert.AreEqual(endPoint.HostName, headers["Host"]);
        Assert.AreEqual("close", headers["Connection"]);
        Assert.IsFalse(headers.ContainsKey("Authorization"));
        Assert.IsFalse(headers.ContainsKey("Content-Length"));
        Assert.IsFalse(headers.ContainsKey("X-ProtocolVersion"));
    }

    private static async Task ReplyToClockProbe(TcpListener listener, X509Certificate2 certificate,
        VpnEndPoint endPoint, DateTimeOffset serverTime, TaskCompletionSource received, Task release,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var tls = new SslStream(client.GetStream());
        await ReadClockProbe(tls, certificate, endPoint, cancellationToken);
        received.SetResult();
        await release.WaitAsync(cancellationToken);
        await tls.WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 401 Unauthorized\r\nDate: {serverTime:R}\r\nWWW-Authenticate: Bearer\r\n" +
            "Content-Length: 0\r\nConnection: close\r\n\r\n"), cancellationToken);
    }

    private static async Task WithholdClockResponse(TcpListener listener, X509Certificate2 certificate,
        VpnEndPoint endPoint, TaskCompletionSource received, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var tls = new SslStream(client.GetStream());
        await ReadClockProbe(tls, certificate, endPoint, cancellationToken);
        received.SetResult();
        try {
            Assert.AreEqual(0, await tls.ReadAsync(new byte[1], cancellationToken),
                "The clock probe must contain no application payload.");
        }
        catch (IOException) {
            // Cancellation may abort TLS instead of sending close_notify. Both release the held connection.
        }
    }
}

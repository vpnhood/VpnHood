using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Http3;
using VpnHood.Core.Server.ProbeResistance;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.ProbeResistance;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3AdmissionBudgetTest : TestBase
{
    [TestMethod]
    public void TCP_and_distinct_QUIC_connections_can_share_a_tuple_without_replacing_each_other()
    {
        var budget = new UnauthenticatedConnections { MaxCount = 4 };
        var shared = EndPoints("127.0.0.1");
        var tcp = new TestConnection();
        var firstQuic = new TestConnection();
        var secondQuic = new TestConnection();
        var firstIdentity = new object();
        var secondIdentity = new object();
        var otherAddress = new TestConnection();
        budget.Add(shared, tcp);
        budget.Add(shared, firstIdentity, firstQuic);
        budget.Add(shared, secondIdentity, secondQuic);
        budget.Add(EndPoints("127.0.0.2"), otherAddress);
        Assert.AreEqual(4, budget.Count);
        Assert.IsFalse(tcp.IsDisposed || firstQuic.IsDisposed || secondQuic.IsDisposed || otherAddress.IsDisposed);

        // Existing TCP replacement and stale-removal behavior stays intact beside the identity keys.
        var replacement = new TestConnection();
        budget.Add(shared, replacement);
        budget.Remove(shared, tcp);
        budget.Remove(firstIdentity, secondQuic);
        Assert.IsTrue(tcp.IsDisposed);
        Assert.AreEqual(4, budget.Count);
        Assert.IsFalse(firstQuic.IsDisposed || secondQuic.IsDisposed || replacement.IsDisposed);

        // All transports still share one address budget. Its oldest remaining connection is now QUIC.
        var newcomer = new TestConnection();
        budget.Add(EndPoints("127.0.0.3"), newcomer);
        Assert.IsTrue(firstQuic.IsDisposed);
        Assert.IsFalse(secondQuic.IsDisposed || replacement.IsDisposed || otherAddress.IsDisposed || newcomer.IsDisposed);
        Assert.AreEqual(4, budget.Count);
        budget.Remove(firstIdentity, firstQuic);
        Assert.AreEqual(4, budget.Count);
        budget.Remove(secondIdentity, secondQuic);
        budget.Remove(shared, replacement);
        Assert.AreEqual(2, budget.Count);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task QUIC_connections_share_the_cap_and_a_valid_proof_does_not_admit_later_unsigned_streams()
    {
        if (!QuicConnection.IsSupported || !QuicListener.IsSupported)
            Assert.Inconclusive("QUIC is not supported on this platform.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var cancellationToken = deadline.Token;
        using var accessManager = await TestHelper.CreateAccessManager();
        accessManager.ServerConfig.SessionOptions.MaxUnauthenticatedConnectionCount = 1;
        accessManager.ServerConfig.SessionOptions.MinClientProtocolVersion = 15;
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        var endPoint = server.ServerHost.QuicEndPoints.Single();
        var certificateHash = accessManager.DefaultCert.GetCertHashString();

        await using var oldest = await Connect(endPoint, token, certificateHash, cancellationToken);
        await AssertEqualsWait(1, () => server.UnauthenticatedConnectionCount);
        await using var waiting = await oldest.OpenOutboundStreamAsync(QuicStreamType.Bidirectional,
            cancellationToken);
        await waiting.WriteAsync("G"u8.ToArray(), cancellationToken);
        await using var current = await Connect(endPoint, token, certificateHash, cancellationToken);
        var eviction = await Assert.ThrowsExactlyAsync<QuicException>(async () => {
            await waiting.ReadAsync(new byte[1], cancellationToken);
        });
        Assert.AreEqual(QuicError.ConnectionAborted, eviction.QuicError);
        Assert.AreEqual(Http3Protocol.ExcessiveLoad, eviction.ApplicationErrorCode);
        Assert.AreEqual(1, server.UnauthenticatedConnectionCount);

        await SendSignedStatus(current, token, cancellationToken);
        await AssertEqualsWait(0, () => server.UnauthenticatedConnectionCount);

        // The transport has left the unauthenticated budget, but each new stream still needs its
        // own proof. This raw HTTP/1 request must never reach the custom tunnel's HTTP/1 responder.
        await using var unsigned = await current.OpenOutboundStreamAsync(QuicStreamType.Bidirectional,
            cancellationToken);
        await unsigned.WriteAsync(Encoding.ASCII.GetBytes(
            $"POST / HTTP/1.1\r\nHost: {token.ServerToken.HostName}\r\n" +
            "X-ProtocolVersion: 15\r\nX-BinaryStream: None\r\nContent-Length: 0\r\n\r\n"), cancellationToken);
        unsigned.CompleteWrites();
        var rejection = await Assert.ThrowsExactlyAsync<QuicException>(async () => {
            await unsigned.ReadAsync(new byte[16], cancellationToken);
        });
        Assert.AreEqual(QuicError.StreamAborted, rejection.QuicError);
        Assert.AreEqual(Http3Protocol.RequestIncomplete, rejection.ApplicationErrorCode);
        Assert.AreEqual(0, server.UnauthenticatedConnectionCount);
    }

    private static IPEndPointPair EndPoints(string remoteAddress) =>
        new(IPEndPoint.Parse("127.0.0.1:443"), new IPEndPoint(IPAddress.Parse(remoteAddress), 12345));

    private static ValueTask<QuicConnection> Connect(IPEndPoint endPoint, Token token, string certificateHash,
        CancellationToken cancellationToken) => QuicConnection.ConnectAsync(new QuicClientConnectionOptions {
            RemoteEndPoint = endPoint,
            DefaultCloseErrorCode = Http3Protocol.NoError,
            DefaultStreamErrorCode = Http3Protocol.RequestCancelled,
            MaxInboundUnidirectionalStreams = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions {
                TargetHost = token.ServerToken.HostName,
                ApplicationProtocols = [SslApplicationProtocol.Http3],
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate?.GetCertHashString() == certificateHash
            }
        }, cancellationToken);

    private static async Task SendSignedStatus(QuicConnection connection, Token token,
        CancellationToken cancellationToken)
    {
        var request = new SessionStatusRequest {
            RequestId = Guid.NewGuid().ToString(),
            SessionId = ulong.MaxValue,
            SessionKey = new byte[16]
        };
        var payload = StreamUtils.ObjectToJsonBuffer(request);
        var secret = token.ServerToken.Secret ?? throw new InvalidOperationException("The token has no proof secret.");
        var proof = new RequestProof(secret).Create(15,
            DateTimeOffset.UtcNow);
        var header = Encoding.ASCII.GetBytes(
            $"POST / HTTP/1.1\r\nHost: {token.ServerToken.HostName}\r\nX-ProtocolVersion: 15\r\n" +
            $"Authorization: Bearer {proof}\r\nX-BinaryStream: None\r\nContent-Length: {payload.Length + 2}\r\n\r\n");
        byte[] bytes = [.. header, 1, request.RequestCode, .. payload.Span];
        await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional,
            cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        stream.CompleteWrites();
        using var response = await HttpUtils.ReadResponse(stream, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await StreamUtils.ReadMessageAsync(stream, cancellationToken);
        var session = JsonSerializer.Deserialize<SessionResponse>(json);
        Assert.IsNotNull(session);
        Assert.AreEqual(SessionErrorCode.AccessError, session.ErrorCode);
    }

    private sealed class TestConnection : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}

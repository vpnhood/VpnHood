using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.ProbeResistance;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests.Servers;

public partial class WebFrontTest
{
    [TestMethod]
    [DataRow("missing", false)]
    [DataRow("missing", true)]
    [DataRow("malformed", false)]
    [DataRow("malformed", true)]
    [DataRow("invalid-signature", false)]
    [DataRow("invalid-signature", true)]
    [DataRow("wrong-host", false)]
    [DataRow("wrong-host", true)]
    [DataRow("expired", false)]
    [DataRow("expired", true)]
    public async Task Protocol15_rejects_bad_proofs_before_body_or_upgrade(string failure, bool upgrade)
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var proof = CreateRejectedProof(token, failure);

        // The POST declares a body but sends none. Waiting for it would miss this helper's deadline.
        using var response = await SendAuthenticationHead(token, upgrade, 15, proof, TestCt);
        AssertPlainUnauthorized(response);
        Assert.IsEmpty(server.SessionManager.Sessions);
    }

    [TestMethod]
    [DataRow(8, false)]
    [DataRow(8, true)]
    [DataRow(14, false)]
    [DataRow(14, true)]
    public async Task Protocol15_minimum_rejects_legacy_headers_before_body_or_upgrade(int httpVersion, bool upgrade)
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.SessionOptions.MinClientProtocolVersion = 15;
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);

        using var response = await SendAuthenticationHead(token, upgrade, httpVersion, proof: null, TestCt);
        AssertPlainUnauthorized(response);
        Assert.IsEmpty(server.SessionManager.Sessions);
    }

    [TestMethod]
    [DataRow(0, 14)]
    [DataRow(9, 8)]
    [DataRow(14, 8)]
    [DataRow(14, 14)]
    [DataRow(15, 15)]
    public async Task Protocol15_upgrade_requires_proof_while_lower_minimums_keep_legacy_compatibility(
        int minimumVersion, int httpVersion)
    {
        using var accessManager = TestHelper.CreateAccessManager();
        if (minimumVersion != 0)
            accessManager.ServerConfig.SessionOptions.MinClientProtocolVersion = minimumVersion;
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        var proof = httpVersion == 15
            ? new RequestProof(token.ServerToken.Secret ?? throw new InvalidOperationException("The token has no farm secret."))
                .Create(token.ServerToken.HostName, 15, DateTimeOffset.UtcNow)
            : null;

        using var response = await SendAuthenticationHead(token, upgrade: true, httpVersion, proof, TestCt);
        Assert.AreEqual(HttpStatusCode.SwitchingProtocols, response.StatusCode);
        Assert.IsTrue(response.Headers.Contains("Sec-WebSocket-Accept"));
    }

    [TestMethod]
    [DataRow(15, 15, false)]
    [DataRow(15, 14, false)]
    [DataRow(14, 14, true)]
    public async Task Protocol15_hello_omits_the_farm_secret_even_for_an_older_binary_session(
        int httpVersion, int binaryVersion, bool sendsSecret)
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.SessionProtocolVersion = binaryVersion;
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        using var tcpClient = new TcpClient();
        await using var stream = await ConnectTls(tcpClient, token);
        var clientInfo = new ClientInfo {
            ClientId = Guid.NewGuid().ToString(),
            ClientVersion = "1.0.0",
            MinProtocolVersion = 8,
            MaxProtocolVersion = 15,
            UserAgent = "RequestProofTest"
        };
        var hello = new HelloRequest {
            RequestId = UniqueIdFactory.Create(),
            EncryptedClientId = VhUtils.EncryptClientId(clientInfo.ClientId, token.Secret),
            ClientInfo = clientInfo,
            TokenId = token.TokenId
        };
        var body = StreamUtils.ObjectToJsonBuffer(hello);
        var proof = httpVersion == 15
            ? new RequestProof(token.ServerToken.Secret!).Create(token.ServerToken.HostName, 15, DateTimeOffset.UtcNow)
            : null;
        var header = BuildAuthenticationHead(token, upgrade: false, httpVersion, proof, body.Length + 2);
        byte[] request = [.. Encoding.ASCII.GetBytes(header), 1, hello.RequestCode, .. body.Span];
        await stream.WriteAsync(request, TestCt);

        using var response = await HttpUtils.ReadResponse(stream, TestCt);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await StreamUtils.ReadMessageAsync(stream, TestCt);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(sendsSecret, document.RootElement.TryGetProperty("ServerSecret", out _));
        var helloResponse = JsonSerializer.Deserialize<HelloResponse>(json);
        Assert.IsNotNull(helloResponse);
        Assert.AreEqual(SessionErrorCode.Ok, helloResponse.ErrorCode);
        Assert.AreEqual(binaryVersion, helloResponse.ProtocolVersion);
        if (sendsSecret)
            CollectionAssert.AreEqual(token.ServerToken.Secret, helloResponse.ServerSecret);
        else
            Assert.IsNull(helloResponse.ServerSecret);
    }

    [TestMethod]
    public async Task Protocol15_secret_rotation_updates_the_proof_gate()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        var oldProof = new RequestProof(token.ServerToken.Secret!).Create(token.ServerToken.HostName, 15,
            DateTimeOffset.UtcNow);
        using var before = await SendAuthenticationHead(token, upgrade: true, 15, oldProof, TestCt);
        Assert.AreEqual(HttpStatusCode.SwitchingProtocols, before.StatusCode);

        var newSecret = RandomNumberGenerator.GetBytes(16);
        server.SessionManager.ServerSecret = newSecret;
        using var oldKey = await SendAuthenticationHead(token, upgrade: true, 15, oldProof, TestCt);
        AssertPlainUnauthorized(oldKey);
        var newProof = new RequestProof(newSecret).Create(token.ServerToken.HostName, 15, DateTimeOffset.UtcNow);
        using var newKey = await SendAuthenticationHead(token, upgrade: true, 15, newProof, TestCt);
        Assert.AreEqual(HttpStatusCode.SwitchingProtocols, newKey.StatusCode);
    }

    private static string? CreateRejectedProof(Token token, string failure)
    {
        var now = DateTimeOffset.UtcNow;
        var proof = new RequestProof(token.ServerToken.Secret!);
        return failure switch {
            "missing" => null,
            "malformed" => "not-a-jwt",
            "invalid-signature" => new RequestProof(RandomNumberGenerator.GetBytes(16))
                .Create(token.ServerToken.HostName, 15, now),
            "wrong-host" => proof.Create("other.example.com", 15, now),
            // Signed with the real farm key, so this reaches expiry validation, not just signature rejection.
            "expired" => proof.Create(token.ServerToken.HostName, 15, now.AddMinutes(-10)),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
    }

    private async Task<HttpResponseMessage> SendAuthenticationHead(Token token, bool upgrade, int httpVersion,
        string? proof, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        await using var stream = await ConnectTls(tcpClient, token);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            BuildAuthenticationHead(token, upgrade, httpVersion, proof, contentLength: 1024)), cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        return await HttpUtils.ReadResponse(stream, deadline.Token);
    }

    private static string BuildAuthenticationHead(Token token, bool upgrade, int httpVersion, string? proof,
        int contentLength)
    {
        var authorization = proof == null ? "" : $"Authorization: Bearer {proof}\r\n";
        var bodyOrUpgrade = upgrade
            ? "Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\n" +
              "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n"
            : $"X-BinaryStream: None\r\nContent-Length: {contentLength}\r\n";
        return $"{(upgrade ? "GET" : "POST")} /request-proof HTTP/1.1\r\n" +
               $"Host: {token.ServerToken.HostName}\r\nX-ProtocolVersion: {httpVersion}\r\n" +
               authorization + bodyOrUpgrade + "\r\n";
    }

    private static void AssertPlainUnauthorized(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, response.Content.Headers.ContentLength);
        Assert.AreEqual("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.IsNull(response.Headers.WwwAuthenticate.Single().Parameter);
        Assert.AreEqual("Kestrel", response.Headers.Server.Single().Product?.Name);
        Assert.IsNotNull(response.Headers.Date);
    }
}

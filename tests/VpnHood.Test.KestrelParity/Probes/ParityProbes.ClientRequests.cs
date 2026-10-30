using System.Security.Cryptography;
using System.Text;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // the same for both servers, so the requests in their logs match
    private static readonly Guid PathId = Guid.Parse("2b9a7a4e-5c1d-4d0e-9f3a-6c2e8b1d7f40");
    private static readonly Guid ConnectionId = Guid.Parse("7d3e1c2a-0b4f-4e6d-8a9b-1f2e3d4c5b6a");

    // Requests in a client's form must prove the farm key before any upgrade or body read.
    private static IReadOnlyList<Probe> ClientRequests()
    {
        // made once, so both servers get the same token
        var unregistered = FakeJwt(expired: false);
        var expiredAt = DateTimeOffset.UtcNow.AddHours(-1);
        return [
            Tls("vh-upgrade-v14", t => VhUpgrade(t, 14)),
            Tls("vh-upgrade-v15-none", t => VhUpgrade(t, 15)),
            Tls("vh-upgrade-v15-malformed", t => VhUpgrade(t, 15, "abc.def")),
            Tls("vh-upgrade-v15-unregistered", t => VhUpgrade(t, 15, unregistered)),
            Tls("vh-upgrade-v15-expired", t => VhUpgrade(t, 15,
                t.Proof.Create(t.Sni, 15, expiredAt))),
            Tls("vh-post-v15-expired-no-body", t => VhPost(t, 15, 50,
                t.Proof.Create(t.Sni, 15, expiredAt))),
            Tls("vh-post-v14-fake-session", t => VhPost(t, 14, FakeSessionBody.Length) + FakeSessionBody),
            Tls("vh-post-v15-fake-session", t => VhPost(t, 15, FakeSessionBody.Length, unregistered) + FakeSessionBody),
            new("vh-post-v14-no-body", ProbeKind.Slow, (target, log, cancellationToken) =>
                Shot(target, log, Latin1(VhPost(target, 14, 50)), tls: true, TimeSpan.FromSeconds(45), cancellationToken)),
            Tls("vh-post-v15-no-body", t => VhPost(t, 15, 50))
        ];
    }

    private static string VhPost(ProbeTarget target, int version, int contentLength, string? auth = null) =>
        $"POST /{PathId} HTTP/1.1\r\nHost: {Host(target)}\r\nContent-Length: {contentLength}\r\n" +
        "Content-Type: application/octet-stream\r\nUser-Agent: Hood\r\nX-Buffered: True\r\n" +
        $"X-ProtocolVersion: {version}\r\nX-ConnectionId: {ConnectionId}\r\nX-BinaryStream: None\r\n" +
        (auth != null ? $"Authorization: Bearer {auth}\r\n" : "") + "\r\n";

    private static string VhUpgrade(ProbeTarget target, int version, string? auth = null) =>
        $"GET //{PathId} HTTP/1.1\r\nHost: {Host(target)}\r\nX-Buffered: True\r\nX-ProtocolVersion: {version}\r\n" +
        $"X-ConnectionId: {ConnectionId}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\n" +
        "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
        (auth != null ? $"Authorization: Bearer {auth}\r\n" : "") + "\r\n";

    // a session request with a made-up session: version 1, request code 4 (SessionStatus), its JSON
    private static string FakeSessionBody
    {
        get {
            var json = Encoding.UTF8.GetBytes(
                $"{{\"RequestId\":\"{ConnectionId}\",\"SessionId\":123456789,\"SessionKey\":\"AAAAAAAAAAAAAAAAAAAAAA==\"}}");
            byte[] body = [1, 4, .. BitConverter.GetBytes(json.Length), .. json];
            return Encoding.Latin1.GetString(body);
        }
    }

    private static string FakeJwt(bool expired)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (expired ? 3600 : 0);
        var header = Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"u8.ToArray());
        var payload = Base64Url(Encoding.UTF8.GetBytes($"{{\"sub\":\"x\",\"iat\":{now},\"exp\":{now + 300}}}"));
        return $"{header}.{payload}.{Base64Url(RandomNumberGenerator.GetBytes(32))}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

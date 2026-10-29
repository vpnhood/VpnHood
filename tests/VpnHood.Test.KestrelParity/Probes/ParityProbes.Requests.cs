using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    private const string JsonBody = "{\"status\":\"ok\"}";

    // what browsers and scanners send: ordinary requests, kept connections and pipelined ones
    private static IReadOnlyList<Probe> Requests() => [
        Tls("get-root-browser", t => Browser(t, "GET", "/")),
        Tls("head-root", t => Simple(t, "HEAD", "/")),
        Tls("post-body", t => Simple(t, "POST", "/api/data",
            $"Content-Type: application/json\r\nContent-Length: {JsonBody.Length}\r\n") + JsonBody),
        Tls("robots", t => Simple(t, "GET", "/robots.txt")),
        Tls("favicon", t => Simple(t, "GET", "/favicon.ico")),
        Tls("get-close", t => Simple(t, "GET", "/", "Connection: close\r\n")),
        Tls("options-star", t => Simple(t, "OPTIONS", "*")),
        Tls("connect", "CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n"),
        Tls("absolute-uri", "GET http://example.com/ HTTP/1.1\r\nHost: example.com\r\n\r\n"),
        Tls("plain-ws-upgrade", t => Simple(t, "GET", "/ws",
            "Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n")),
        Tls("expect-continue", "POST / HTTP/1.1\r\nHost: x\r\nContent-Length: 5\r\nExpect: 100-continue\r\n\r\n"),

        Tls("http10", "GET / HTTP/1.0\r\n\r\n"),
        Tls("http10-keepalive", "GET / HTTP/1.0\r\nConnection: keep-alive\r\n\r\n"),
        Sequence("second-request", tls: true, t => [Simple(t, "GET", "/"), Simple(t, "GET", "/robots.txt")]),
        Sequence("post-body-then-get", tls: true, t => [
            Simple(t, "POST", "/api", "Content-Length: 100\r\n") + new string('a', 100), Simple(t, "GET", "/")
        ]),
        Sequence("chunked-then-get", tls: true, t => [
            Simple(t, "POST", "/api", "Transfer-Encoding: chunked\r\n") + "5\r\nhello\r\n0\r\n\r\n", Simple(t, "GET", "/")
        ]),
        Tls("chunked-post", t => Simple(t, "POST", "/api", "Transfer-Encoding: chunked\r\n") + "5\r\nhello\r\n0\r\n\r\n"),
        Tls("pipelined", t => Simple(t, "GET", "/") + Simple(t, "GET", "/favicon.ico")),
        Tls("pipelined-post-get", t => Simple(t, "POST", "/", "Content-Length: 5\r\n") + "hello" + Simple(t, "GET", "/robots.txt")),
        Tls("pipelined-bad-second", t => Simple(t, "GET", "/") + "HELLO WORLD\r\n\r\n"),
        .. Both("pipelined-three", Get("x") + Get("x") + Get("x")),
        .. Both("pipelined-blank-lines-between", Get("x") + "\r\n\r\n" + Get("x")),
        .. Both("pipelined-http10-keepalive", "GET / HTTP/1.0\r\nConnection: keep-alive\r\n\r\n" + Second),
        .. Both("pipelined-chunked-trailer", ChunkedHead + "1\r\nx\r\n0\r\nX-T: a\r\n\r\n" + Second)
    ];

    // each request on one connection after the reply to the one before, then how the connection ends
    private static Probe Sequence(string name, bool tls, Func<ProbeTarget, IReadOnlyList<string>> requests) =>
        new(name, ProbeKind.Quick, async (target, log, cancellationToken) => {
            await using var connection = await ProbeConnection.Open(tls ? target.Tls : target.Http, tls, target.Sni,
                log, cancellationToken);
            foreach (var request in requests(target)) {
                await connection.Send(request, cancellationToken);
                await connection.Read(Quiet, Wait, cancellationToken);
            }
            await connection.WaitEnd(EndWait, cancellationToken);
        });
}

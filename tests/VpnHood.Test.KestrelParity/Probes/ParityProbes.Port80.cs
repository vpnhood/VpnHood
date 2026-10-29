using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // port 80, where HTTPS redirection answers with a 307 to the same URL on the TLS port
    private static IReadOnlyList<Probe> Port80() => [
        Http("p80-head-root", t => Simple(t, "HEAD", "/")),
        Http("p80-get-root", t => Browser(t, "GET", "/")),
        Http("p80-get-path", t => Simple(t, "GET", "/api/status?x=1")),
        Http("p80-acme-unknown", t => Simple(t, "GET", "/.well-known/acme-challenge/abcdefghijklmnop")),
        Http("p80-post", t => Simple(t, "POST", "/", "Content-Length: 5\r\n") + "hello"),
        Http("p80-malformed", "HELLO WORLD\r\n\r\n"),
        Http("p80-no-host", "GET / HTTP/1.1\r\n\r\n"),
        Http("p80-http10", "GET / HTTP/1.0\r\n\r\n"),
        Sequence("p80-second-request", tls: false, t => [Simple(t, "GET", "/"), Simple(t, "GET", "/robots.txt")]),
        Http("p80-absolute", "GET http://example.com/x?y=1 HTTP/1.1\r\nHost: example.com\r\n\r\n"),
        Http("p80-options-star", "OPTIONS * HTTP/1.1\r\nHost: example.com\r\n\r\n"),
        Http("p80-host-port", "GET /a HTTP/1.1\r\nHost: example.com:8080\r\n\r\n"),
        Http("p80-host-ipv6", "GET /a HTTP/1.1\r\nHost: [::1]:80\r\n\r\n"),
        Http("p80-host-upper", "GET /a HTTP/1.1\r\nHost: EXAMPLE.com\r\n\r\n"),
        Http("p80-dots", "GET /a/./b/../c/ HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-dots-above-root", "GET /../../a HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-double-slash", "GET //a//b HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-pct", "GET /a%20b%41%7e%2fc%2F?q=%20%41&r= HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-pct-bad", "GET /a%zz%4 HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-pct-utf8", "GET /caf%C3%A9 HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-pct-bad-utf8", "GET /a%C3%28 HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-raw-utf8", "GET /cafÃ© HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-chars", "GET /a'b(c)*d!e$f&g+h,i;j=k:l@m[n] HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-query-only", "GET /?a=1 HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-empty-query", "GET /a? HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-head-http10", "HEAD / HTTP/1.0\r\n\r\n"),
        Http("p80-close", "GET / HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n"),
        Http("p80-acme-head", "HEAD /.well-known/acme-challenge/abc HTTP/1.1\r\nHost: x\r\n\r\n"),
        Http("p80-long-line", t => LineOf(t, 9000)),
        new("p80-idle", ProbeKind.Slow, async (target, log, cancellationToken) => {
            await using var connection = await ProbeConnection.Open(target.Http, false, target.Sni, log, cancellationToken);
            log.AppendLine("> (nothing)");
            await connection.WaitEnd(TimeSpan.FromSeconds(150), cancellationToken);
        }),
        Http("p80-partial", "GET / HTTP/1.1\r\nHost: x\r\n", ProbeKind.Slow, endWaitSeconds: 45)
    ];
}

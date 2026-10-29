using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // header lines: their syntax, limits and decoding, the Connection and Transfer-Encoding tokens, and the body's
    // length rules
    private static IReadOnlyList<Probe> Headers() => [
        Tls("no-host", "GET / HTTP/1.1\r\n\r\n"),
        Tls("two-hosts", "GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n"),
        Tls("head-no-host", "HEAD / HTTP/1.1\r\n\r\n"),
        Tls("bad-header", "GET / HTTP/1.1\r\nHost: x\r\nNoColonHere\r\n\r\n"),
        Tls("obs-fold", "GET / HTTP/1.1\r\nHost: x\r\nX-A: a\r\n b\r\n\r\n"),
        Tls("space-before-colon", "GET / HTTP/1.1\r\nHost : x\r\n\r\n"),
        Tls("empty-header-name", "GET / HTTP/1.1\r\nHost: x\r\n: v\r\n\r\n"),
        Tls("header-ctl", "GET / HTTP/1.1\r\nHost: x\r\nX-A: a\u0001b\r\n\r\n"),
        Tls("header-del", "GET / HTTP/1.1\r\nHost: x\r\nX-A: a\u007fb\r\n\r\n"),
        Tls("cr-in-header", "GET / HTTP/1.1\r\nHost: x\r\nX-A: a\rb\r\n\r\n"),

        // the limits: 32 KB of header lines with their line ends, and 100 headers
        Tls("big-headers", t => Simple(t, "GET", "/",
            string.Concat(Enumerable.Range(0, 40).Select(i => $"X-Fill-{i}: {new string('b', 1000)}\r\n")))),
        Tls("many-headers", t => Simple(t, "GET", "/",
            string.Concat(Enumerable.Range(0, 120).Select(i => $"X-H{i}: v\r\n")))),
        Tls("headers-32766", HeadersOf(32766)),
        Tls("headers-32767", HeadersOf(32767)),
        Tls("headers-32768", HeadersOf(32768)),
        Tls("headers-32769", HeadersOf(32769)),
        Tls("headers-32770", HeadersOf(32770)),
        Tls("header-count-100", HeaderCount(100)),
        Tls("header-count-101", HeaderCount(101)),
        .. RejectionWithTail("headers-tail",
            $"GET / HTTP/1.1\r\nHost: x\r\nX-A: {new string('a', 33_000)}\r\n\r\n" + new string('b', 300_000)),

        // a value is ASCII or valid UTF-8, and a name a token
        Tls("header-utf8", "GET / HTTP/1.1\r\nHost: x\r\nX-A: cafÃ©\r\n\r\n"),
        Tls("header-name-utf8", "GET / HTTP/1.1\r\nHost: x\r\nX-Ã©: v\r\n\r\n"),
        .. Both("value-ff", "GET / HTTP/1.1\r\nHost: x\r\nX-A: ÿ\r\n\r\n"),
        .. Both("value-latin1-nbsp", "GET / HTTP/1.1\r\nHost: x\r\nX-A: a b\r\n\r\n"),
        .. Both("value-truncated-utf8", "GET / HTTP/1.1\r\nHost: x\r\nX-A: aÃ\r\n\r\n"),
        .. Both("value-overlong-utf8", "GET / HTTP/1.1\r\nHost: x\r\nX-A: À¯\r\n\r\n"),
        .. Both("value-surrogate-utf8", "GET / HTTP/1.1\r\nHost: x\r\nX-A: í \u0080\r\n\r\n"),
        .. Both("value-above-max-utf8", "GET / HTTP/1.1\r\nHost: x\r\nX-A: ô\u0090\u0080\u0080\r\n\r\n"),
        .. Both("user-agent-latin1", "GET / HTTP/1.1\r\nHost: x\r\nUser-Agent: café\r\n\r\n"),
        .. Both("value-valid-utf8", "GET / HTTP/1.1\r\nHost: x\r\nX-A: cafÃ©\r\n\r\n"),
        .. Both("value-4-byte-utf8", "GET / HTTP/1.1\r\nHost: x\r\nX-A: ð\u009f\u0098\u0080\r\n\r\n"),

        // Connection: only spaces and empty elements are skipped, and close wins over keep-alive
        Tls("head-close", "HEAD / HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n"),
        Tls("connection-close-upgrade", "GET / HTTP/1.1\r\nHost: x\r\nConnection: Upgrade, close\r\nUpgrade: x\r\n\r\n"),
        Tls("connection-keepalive-11", "GET / HTTP/1.1\r\nHost: x\r\nConnection: keep-alive\r\n\r\n"),
        .. Both("connection-close-tab-comma", "GET / HTTP/1.1\r\nHost: x\r\nConnection: close\t,x\r\n\r\n"),
        .. Both("connection-close-nbsp", "GET / HTTP/1.1\r\nHost: x\r\nConnection: close \r\n\r\n"),
        .. Both("connection-close-vt", "GET / HTTP/1.1\r\nHost: x\r\nConnection: close\u000b\r\n\r\n"),
        .. Both("connection-close-nel", "GET / HTTP/1.1\r\nHost: x\r\nConnection: close\u0085\r\n\r\n"),
        .. Both("connection-close-space-comma", "GET / HTTP/1.1\r\nHost: x\r\nConnection: close ,x\r\n\r\n"),
        .. Both("http10-close-wins", "GET / HTTP/1.0\r\nHost: x\r\nConnection: keep-alive, close\r\n\r\n" + Second),
        .. Both("http10-close-lines", "GET / HTTP/1.0\r\nHost: x\r\nConnection: keep-alive\r\nConnection: close\r\n\r\n" + Second),

        // HTTP/1.0: no Host needed, and a POST or PUT needs a length unless it asks for an upgrade
        Tls("http10-head", "HEAD / HTTP/1.0\r\n\r\n"),
        Tls("http10-host", "GET / HTTP/1.0\r\nHost: x\r\n\r\n"),
        Tls("http10-post-body", "POST / HTTP/1.0\r\nContent-Length: 5\r\n\r\nhello"),
        .. Both("http10-post", "POST / HTTP/1.0\r\nHost: x\r\n\r\n"),
        .. Both("http10-put", "PUT / HTTP/1.0\r\nHost: x\r\n\r\n"),
        .. Both("http11-post", "POST / HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("http11-put", "PUT / HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("http10-post-upgrade", "POST / HTTP/1.0\r\nHost: x\r\nConnection: upgrade\r\n\r\n"),
        .. Both("http10-put-upgrade", "PUT / HTTP/1.0\r\nHost: x\r\nConnection: upgrade\r\n\r\n"),
        .. Both("http10-upgrade-tab-comma", "POST / HTTP/1.0\r\nHost: x\r\nConnection: upgrade\t,x\r\n\r\n"),
        .. Both("http10-upgrade-nbsp", "POST / HTTP/1.0\r\nHost: x\r\nConnection: upgrade \r\n\r\n"),

        // Content-Length: digits alone, once, and the 30 MB body limit
        Tls("cl-bad", "POST / HTTP/1.1\r\nHost: x\r\nContent-Length: abc\r\n\r\n"),
        Tls("cl-negative", "POST / HTTP/1.1\r\nHost: x\r\nContent-Length: -1\r\n\r\n"),
        Tls("cl-twice-same", "POST / HTTP/1.1\r\nHost: x\r\nContent-Length: 5\r\nContent-Length: 5\r\n\r\nhello"),
        Tls("cl-list", "POST / HTTP/1.1\r\nHost: x\r\nContent-Length: 5, 5\r\n\r\nhello"),
        Tls("cl-huge-digits", "POST / HTTP/1.1\r\nHost: x\r\nContent-Length: 99999999999999999999999\r\n\r\n"),
        Tls("cl-30000000", PostLength(30_000_000, new string('a', 100))),
        Tls("cl-30000001", PostLength(30_000_001, new string('a', 100))),
        .. Both("cl-over-limit", PostLength(30_000_001)),
        .. Both("cl-over-limit-body", PostLength(30_000_001, new string('x', 1000))),
        .. Both("cl-at-limit", PostLength(30_000_000), endWaitSeconds: 10),
        .. Both("cl-partial", PostLength(5, "hel"), endWaitSeconds: 10),
        .. Both("cl-then-get", PostLength(5, "hello") + Second),
        .. RejectionWithTail("body-tail", PostLength(30_000_001, new string('b', 300_000))),

        // Transfer-Encoding: the last token, of those that aren't empty, must be chunked
        Tls("te-and-cl", "POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\nContent-Length: 5\r\n\r\n5\r\nhello\r\n0\r\n\r\n"),
        Tls("te-gzip", "POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: gzip\r\n\r\n"),
        Tls("te-gzip-chunked", PostChunked("gzip, chunked", "0\r\n\r\n")),
        Tls("te-http10", "POST / HTTP/1.0\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n"),
        .. Both("te-chunked", PostChunked("chunked")),
        .. Both("te-chunked-vt", PostChunked("chunked\u000b")),
        .. Both("te-trailing-comma", PostChunked("chunked,")),
        .. Both("te-double-comma", PostChunked("chunked,,")),
        .. Both("te-leading-comma", PostChunked(",chunked")),
        .. Both("te-then-x", PostChunked("chunked, x")),
        .. Both("te-then-x-no-space", PostChunked("chunked,x")),
        .. Both("te-then-x-space-before-comma", PostChunked("chunked , x")),
        .. Both("te-then-xy", PostChunked("chunked, xy")),
        .. Both("te-x", PostChunked("x")),
        .. Both("te-gzip-chunked-x", PostChunked("gzip, chunked, x")),
        .. Both("te-second-header-x",
            "POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\nTransfer-Encoding: x\r\n\r\n0\r\n\r\n" + Second)
    ];

    // header lines of exactly `total` bytes, each with its CRLF, the empty line after them not counted
    private static string HeadersOf(int total)
    {
        const string host = "Host: x\r\n";
        var fill = total - host.Length - "X-F: \r\n".Length;
        return $"GET / HTTP/1.1\r\n{host}X-F: {new string('a', fill)}\r\n\r\n";
    }

    private static string HeaderCount(int count) =>
        "GET / HTTP/1.1\r\nHost: x\r\n" + string.Concat(Enumerable.Range(1, count - 1).Select(i => $"X-H{i}: v\r\n")) + "\r\n";
}

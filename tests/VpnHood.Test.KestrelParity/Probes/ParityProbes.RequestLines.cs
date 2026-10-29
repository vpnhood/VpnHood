using System.Text;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // the request line: its method, target and version, their forms, its line ends and its length limit
    private static IReadOnlyList<Probe> RequestLines() => [
        Tls("malformed-line", "HELLO WORLD\r\n\r\n"),
        Tls("lowercase-method", "get / HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("bad-version", "GET / HTTP/9.9\r\nHost: x\r\n\r\n"),
        Tls("bare-lf", "GET / HTTP/1.1\nHost: x\n\n"),
        Tls("long-request-line", t => Simple(t, "GET", "/" + new string('a', 9000))),
        Tls("random-bytes", Encoding.Latin1.GetString(Fixed(64, withLf: false))),
        Tls("random-bytes-lf", Encoding.Latin1.GetString(Fixed(64, withLf: true))),
        Tls("line-8190", t => LineOf(t, 8190)),
        Tls("line-8191", t => LineOf(t, 8191)),
        Tls("line-8192", t => LineOf(t, 8192)),
        Tls("line-8193", t => LineOf(t, 8193)),
        .. Both("line-lf-8191", $"GET /{new string('a', 8191 - 14)} HTTP/1.1\nHost: x\r\n\r\n"),
        .. Both("line-lf-8192", $"GET /{new string('a', 8192 - 14)} HTTP/1.1\nHost: x\r\n\r\n"),
        .. RejectionWithTail("line-tail", $"GET /{new string('a', 9000)} HTTP/1.1\r\nHost: x\r\n\r\n" + new string('b', 300_000)),

        Tls("two-spaces", "GET  / HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("tab-separator", "GET\t/ HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("leading-crlf", "\r\nGET / HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("leading-crs", "\r\rGET / HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("method-ctl", "G\u0001T / HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("method-long", new string('A', 300) + " / HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("http20", "GET / HTTP/2.0\r\nHost: x\r\n\r\n"),
        Tls("http09", "GET / HTTP/0.9\r\nHost: x\r\n\r\n"),
        Tls("https-version", "GET / HTTPS/1.1\r\nHost: x\r\n\r\n"),
        Tls("version-lowercase", "GET / http/1.1\r\nHost: x\r\n\r\n"),
        Tls("no-version", "GET /\r\nHost: x\r\n\r\n"),

        Tls("target-utf8", "GET /cafÃ© HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("target-ctl", "GET /a\u0001b HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("target-chars", "GET /a|b{c}^d`e\"f<g>h HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("target-fragment", "GET /a#b HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("target-no-slash", "GET a HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("get-star", "GET * HTTP/1.1\r\nHost: x\r\n\r\n"),
        Tls("connect-path", "CONNECT /x HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("long-redirect", $"GET {string.Concat(Enumerable.Repeat("/a", 3900))} HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("acme-query", "GET /.well-known/acme-challenge/unknown?x=1 HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("acme-absolute", "GET http://x/.well-known/acme-challenge/unknown?x=1 HTTP/1.1\r\nHost: x\r\n\r\n"),

        // the absolute form: only a lowercase scheme makes one, and the Host must repeat its authority
        Tls("absolute-host-mismatch", "GET http://example.com/ HTTP/1.1\r\nHost: other.com\r\n\r\n"),
        Tls("absolute-https", "GET https://example.com/a HTTP/1.1\r\nHost: example.com\r\n\r\n"),
        Tls("absolute-ftp", "GET ftp://example.com/ HTTP/1.1\r\nHost: example.com\r\n\r\n"),
        .. Both("absolute-uppercase-host", "GET http://EXAMPLE.com/ HTTP/1.1\r\nHost: EXAMPLE.com\r\n\r\n"),
        .. Both("absolute-target-port", "GET http://example.com:80/ HTTP/1.1\r\nHost: example.com\r\n\r\n"),
        .. Both("absolute-host-port", "GET http://example.com/ HTTP/1.1\r\nHost: example.com:80\r\n\r\n"),
        .. Both("scheme-uppercase", "GET HTTP://x/ HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("scheme-capitalized", "GET Http://x/ HTTP/1.1\r\nHost: x\r\n\r\n"),

        // the authority form, which only CONNECT takes, and its characters
        .. Both("authority-get", "GET a:x HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("connect-host-mismatch", "CONNECT a:443 HTTP/1.1\r\nHost: b:443\r\n\r\n"),
        .. Both("authority-at", "GET a@b HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("authority-underscore", "GET a_b HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("authority-tilde", "GET a~b HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("authority-equals", "GET a=b HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("authority-bang", "GET a!b HTTP/1.1\r\nHost: x\r\n\r\n"),
        .. Both("authority-percent", "GET a%41 HTTP/1.1\r\nHost: x\r\n\r\n")
    ];

    // the same bytes for both servers; withLf puts a line end in the middle, as a request line's end
    private static byte[] Fixed(int count, bool withLf)
    {
        var bytes = new byte[count];
        new Random(42).NextBytes(bytes);
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] is (byte)'\n' or (byte)'\r')
                bytes[i] = (byte)'x';
        if (withLf)
            bytes[count / 2] = (byte)'\n';
        return bytes;
    }

    // a request line of exactly `length` characters before its CRLF
    private static string LineOf(ProbeTarget target, int length) =>
        $"GET /{new string('a', length - "GET / HTTP/1.1".Length)} HTTP/1.1\r\nHost: {Host(target)}\r\n\r\n";
}

using System.Security.Authentication;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // a whole 5-byte handshake record, as a TLS client sent where HTTP was expected
    private const string Record = "\u0016\u0003\u0001\u0000\u0005\u0001\u0000\u0000\u0001\u0000";
    private const string Preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n";

    // one real ClientHello for every probe that sends one, so both servers get the same bytes
    private static readonly Lazy<Task<byte[]>> RealClientHello = new(() =>
        TlsHandshake.CaptureClientHello("example.com", null, SslProtocols.None, CancellationToken.None));

    // what other protocols send first: a TLS record, binary probes such as RDP's and SMB's, and HTTP/2's preface.
    // Kestrel refuses a request line at once once it holds a zero byte, before its line end; other bytes wait
    private static IReadOnlyList<Probe> BinaryStarts() => [
        .. Enumerable.Range(2, 9).SelectMany(n => Both($"record-prefix-{n}", Record[..n])),
        .. Both("record-tls12", "\u0016\u0003\u0003" + Record[3..]),
        .. Both("record-ssl3", "\u0016\u0003\u0000" + Record[3..]),
        .. Both("record-version-39", "\u0016\u0003\u0009" + Record[3..]),
        .. Both("record-version-2", "\u0016\u0002\u0001" + Record[3..]),
        .. Both("record-server-hello-type", Record[..5] + "\u0002" + Record[6..]),
        .. Both("record-length-0", "\u0016\u0003\u0001\u0000\u0000"),
        .. Both("record-length-0-more", "\u0016\u0003\u0001\u0000\u0000\u0001\u0000"),
        .. Both("record-application-data", "\u0017" + Record[1..]),
        .. Both("record-long-partial", "\u0016\u0003\u0001\u0002\u0000\u0001\u0000\u0001ü\u0003\u0003" +
                                       string.Concat(Enumerable.Range(11, 29).Select(x => (char)x))),
        .. Both("record-prefix-2-lf", "\u0016\u0003\n"),
        .. Both("record-prefix-3-lf", "\u0016\u0003\u0001\n"),
        .. Both("not-tls-lf", "\u0016\n"),
        .. ClientHellos(),

        .. Both("bytes-1-2-3", "\u0001\u0002\u0003"),
        .. Both("bytes-1-2-3-4", "\u0001\u0002\u0003\u0004"),
        .. Both("high-bytes", "ÿÿÿÿ"),
        .. Both("del-bytes", "\u007f\u007f\u007f\u007f"),
        .. Both("escape-sequence", "\u001b[0m"),
        .. Both("at-2", "@@"),
        .. Both("at-3", "@@@"),
        .. Both("at-4", "@@@@"),
        .. Both("token-then-at-2", "A@"),
        .. Both("token-then-at-3", "AA@"),
        .. Both("token-then-at-4", "AAA@"),
        .. Both("token-then-at-5", "AAAA@"),
        .. Both("token-then-at-8", "AAAAAAA@"),
        .. Both("token-then-at-13", "AAAAAAAAAAAA@"),
        .. Both("token-4", "AAAA"),
        .. Both("token-20", new string('A', 20)),
        .. Both("token-100", new string('A', 100)),
        .. Both("get-ctl", "GET\u0001"),
        .. Both("get-space", "GET "),
        .. Both("get-slash", "GET /"),
        .. Both("get-target-ctl", "GET /\u0001"),
        .. Both("get-target-high", "GET /\u0080"),
        .. Both("get-no-version", "GET / "),
        .. Both("get-bad-version-no-lf", "GET / HTTX/1.1"),
        .. Both("get-version-no-lf", "GET / HTTP/1.1"),
        .. Both("get-version-cr", "GET / HTTP/1.1\r"),
        .. Both("get-two-spaces-no-lf", "GET  "),
        .. Both("space-get", " GET"),
        .. Both("tab-get", "\tGET"),
        .. Both("lowercase-get-space", "get "),
        .. Both("rdp", "\u0003\u0000\u0000\u0013\u000eà\u0000\u0000\u0000\u0000\u0000\u0001\u0000\u0008\u0000\u0003\u0000\u0000\u0000"),
        .. Both("smb", "\u0000\u0000\u0000\u0085ÿSMBr\u0000\u0000\u0000\u0000"),
        .. Both("ssh", "SSH-2.0-OpenSSH_9.6"),

        .. Both("zero-alone", "\u0000"),
        .. Both("zero-three", "\u0000\u0000\u0000"),
        .. Both("zero-after-g", "G\u0000"),
        .. Both("zero-in-method", "G\u0000ET"),
        .. Both("zero-in-target", "GET /\u0000"),
        .. Both("zero-after-version", "GET / HTTP/1.1\u0000"),
        .. Both("zero-after-blank-line", "\r\n\u0000"),
        .. Both("zero-second-request", Get("x") + "G\u0000"),
        .. Both("zero-header-start", "GET / HTTP/1.1\r\nHost: x\r\n\u0000"),
        .. Both("zero-in-header-name", "GET / HTTP/1.1\r\nHost: x\r\nX-\u0000"),
        .. Both("zero-in-header-value", "GET / HTTP/1.1\r\nHost: x\r\nX-A: \u0000"),
        .. Both("zero-in-trailer", ChunkedHead + "0\r\nX-T: \u0000"),

        // HTTP/2's preface: Kestrel answers it with a GOAWAY only as a connection's first bytes on a plain port
        .. Both("h2-preface", Preface),
        .. Both("h2-preface-settings", Preface + "\u0000\u0000\u0000\u0004\u0000\u0000\u0000\u0000\u0000"),
        .. Both("h2-preface-no-final-crlf", "PRI * HTTP/2.0\r\n\r\nSM\r\n"),
        .. Both("h2-preface-wrong-magic", "PRI * HTTP/2.0\r\n\r\nXX\r\n\r\n"),
        .. Both("h2-preface-after-blank-line", "\r\n" + Preface),
        .. Both("h2-preface-second-request", Get("x") + Preface),
        .. Both("h2-preface-slash-target", "PRI / HTTP/2.0\r\n\r\nSM\r\n\r\n"),
        .. Both("h2-request-line", "PRI * HTTP/2.0\r\n"),
        .. Both("h2-request-line-blank", "PRI * HTTP/2.0\r\n\r\n"),
        .. Both("h2-request-line-host", "PRI * HTTP/2.0\r\nHost: x\r\n\r\n"),
        .. Both("h2-request-line-lf", "PRI * HTTP/2.0\n"),
        .. Both("h2-request-line-lowercase", "pri * HTTP/2.0\r\n"),
        .. Both("h2-request-line-no-lf", "PRI * HTTP/2.0"),
        .. Both("pri-http11", "PRI * HTTP/1.1\r\n\r\n"),

        // a TLS record's start without a zero byte: Kestrel waits for a line end, then its 408
        .. Both("tls-start", "\u0016\u0003", ProbeKind.Slow, endWaitSeconds: 40),
        .. Both("tls-start-after-blank-line", "\r\n\u0016\u0003", ProbeKind.Slow, endWaitSeconds: 40),
        .. Both("tls-start-second-request", Get("x") + "\u0016\u0003", ProbeKind.Slow, endWaitSeconds: 40),
        .. BothPieces("tls-start-split", [("\u0016", 300), ("\u0003", 0)], ProbeKind.Slow, endWaitSeconds: 40),
        .. BothPieces("tls-start-after-request", [(Get("x"), 500), ("\u0016\u0003", 0)], ProbeKind.Slow, endWaitSeconds: 40),
        .. BothPieces("tls-start-after-blank-line-packet", [("\r\n", 300), ("\u0016\u0003", 0)], ProbeKind.Slow,
            endWaitSeconds: 40),
        .. BothPieces("tls-start-third-byte-later", [("\u0016\u0003", 300), ("\u0001", 0)], ProbeKind.Slow,
            endWaitSeconds: 40)
    ];

    private static IEnumerable<Probe> ClientHellos()
    {
        foreach (var tls in new[] { true, false }) {
            var suffix = tls ? "tls" : "http";
            yield return ClientHelloShot($"client-hello-{suffix}", tls, hello => hello);
            yield return ClientHelloShot($"client-hello-before-lf-{suffix}", tls, BeforeLineFeed);
            yield return ClientHelloShot($"client-hello-first-16-{suffix}", tls, hello => hello[..16]);
        }
    }

    private static Probe ClientHelloShot(string name, bool tls, Func<byte[], byte[]> part) =>
        new(name, ProbeKind.Quick, async (target, log, cancellationToken) => {
            var hello = await RealClientHello.Value.WaitAsync(cancellationToken);
            await Shot(target, log, part(hello), tls, EndWait, cancellationToken);
        });

    private static byte[] BeforeLineFeed(byte[] bytes)
    {
        var lineFeed = Array.IndexOf(bytes, (byte)'\n');
        return lineFeed < 0 ? bytes : bytes[..lineFeed];
    }
}

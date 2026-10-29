using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    private const string FirstChunked = "POST /first HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n";
    private const string LastChunk = "0\r\n\r\n" + Second;

    // chunked bodies, which the reply leaves unread and the server then drains: a size of at most eight hex digits,
    // extensions and data ending in CRLF, trailers with the header limits, and a bad byte judged only once the
    // next one has come
    private static IReadOnlyList<Probe> Framing() => [
        Tls("chunked-bad-size", ChunkedHead + "zz\r\nhello\r\n0\r\n\r\n"),
        Tls("chunked-ext-trailer", ChunkedHead + "5;a=b\r\nhello\r\n0\r\nX-T: v\r\n\r\n"),
        .. Both("chunk-data-lf", FirstChunked + "1\r\nx\n0\r\n\r\n" + Second),
        .. Both("chunk-size-lf", FirstChunked + "1\nx\r\n0\r\n\r\n" + Second),
        .. Both("chunk-two-crs", FirstChunked + "1\r\r\nx\r\n0\r\n\r\n" + Second),
        .. Both("chunk-leading-space", FirstChunked + " 1\r\nx\r\n0\r\n\r\n" + Second),
        .. Both("chunk-trailing-space", FirstChunked + "1 \r\nx\r\n0\r\n\r\n" + Second),
        .. Both("chunk-semicolon-first", ChunkedHead + ";\r\nx\r\n" + LastChunk),
        .. Both("chunk-size-overflow", FirstChunked + "1\r\nx\r\n7fffffffffffffff\r\n"),
        .. Both("chunk-size-17-digits", FirstChunked + "10000000000000000\r\n"),
        .. Both("chunk-size-10-digits", ChunkedHead + "0000000001\r\nx\r\n" + LastChunk),
        .. Both("chunk-size-11-digits", ChunkedHead + "00000000001\r\nx\r\n" + LastChunk),
        .. Both("chunk-size-8-digits", ChunkedHead + "00000001\r\nx\r\n" + LastChunk, endWaitSeconds: 10),
        .. Both("chunk-size-9-zeros-crlf", ChunkedHead + "000000000\r\n", endWaitSeconds: 10),
        .. Both("chunk-size-7fffffff", ChunkedHead + "7FFFFFFF\r\n", endWaitSeconds: 10),
        .. Both("chunk-size-80000000", ChunkedHead + "80000000\r\n", endWaitSeconds: 10),
        .. Both("chunk-size-ffffffff", ChunkedHead + "FFFFFFFF\r\n", endWaitSeconds: 10),
        .. Both("chunk-size-80000000-extension", ChunkedHead + "80000000;a\r\n", endWaitSeconds: 10),
        .. Both("chunk-7fffffff-data", ChunkedHead + "7fffffff\r\nx", endWaitSeconds: 10),
        .. Both("chunk-1d905c0-data", ChunkedHead + "1d905c0\r\nx", endWaitSeconds: 10),

        // a chunk's size line or data end that stops at a byte Kestrel can't judge yet: it waits for the next
        // byte, until its drain gives up
        .. Both("partial-size-9-digits", ChunkedHead + "000000000", endWaitSeconds: 10),
        .. Both("partial-size-bad-first", ChunkedHead + "x", endWaitSeconds: 10),
        .. Both("partial-size-digit-then-bad", ChunkedHead + "1x", endWaitSeconds: 10),
        .. Both("partial-data-end-bad", ChunkedHead + "1\r\nxy", endWaitSeconds: 10),
        .. Both("partial-trailer-tls-start", ChunkedHead + "0\r\n\u0016\u0003", endWaitSeconds: 10),
        .. Both("partial-extension-cr", ChunkedHead + "1;a\r", endWaitSeconds: 10),
        .. Both("partial-extension-lf", ChunkedHead + "1;a\n", endWaitSeconds: 10),

        // extensions: any length, against the body limit, ending in CRLF and nothing else
        .. Both("extension-empty", ChunkedHead + "1;\r\nx\r\n" + LastChunk),
        .. Both("extension-cr", ChunkedHead + "1;a\rb\r\nx\r\n" + LastChunk),
        .. Both("extension-lf", ChunkedHead + "1;a\nb\r\nx\r\n" + LastChunk),
        .. Both("extension-cr-cr-lf", ChunkedHead + "1;a\r\r\nx\r\n" + LastChunk, endWaitSeconds: 10),
        .. Both("extension-long", ChunkedHead + "1;" + new string('a', 5000) + "\r\nx\r\n" + LastChunk),

        // trailers: the header limits left after the headers, and their line rules
        .. Both("trailer-valid", FirstChunked + "0\r\nX-T: v\r\n\r\n" + Second),
        .. Both("trailer-no-colon", FirstChunked + "0\r\nnot-a-header\r\n\r\n" + Second),
        .. Both("trailer-lf", ChunkedHead + "0\r\nX-T: v\n\r\n" + Second),
        .. Both("trailer-end-lf", ChunkedHead + "0\r\n\n" + Second),
        .. Both("trailer-last-lf", ChunkedHead + "0\r\nX-T: v\r\n\n" + Second),
        .. Both("trailer-ff", ChunkedHead + "0\r\nX-T: ÿ\r\n\r\n" + Second),
        .. Both("trailer-101", FirstChunked + "0\r\n" +
                               string.Concat(Enumerable.Range(0, 101).Select(i => $"X-{i}: v\r\n")) + "\r\n" + Second),
        .. Both("trailer-count-98", FirstChunked + "0" + string.Concat(Enumerable.Repeat("\r\nX-T: v", 98)) + "\r\n\r\n" + Second),
        .. Both("trailer-count-99", FirstChunked + "0" + string.Concat(Enumerable.Repeat("\r\nX-T: v", 99)) + "\r\n\r\n" + Second),
        .. Both("trailer-33k", FirstChunked + $"0\r\nX-T: {new string('v', 33_000)}\r\n\r\n" + Second),
        .. Both("trailer-size-32729", FirstChunked + $"0\r\nX-T: {new string('v', 32_722)}\r\n\r\n" + Second),
        .. Both("trailer-size-32730", FirstChunked + $"0\r\nX-T: {new string('v', 32_723)}\r\n\r\n" + Second)
    ];
}

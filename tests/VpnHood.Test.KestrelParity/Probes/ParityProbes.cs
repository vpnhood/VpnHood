using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

// Every probe, each sent the same way to the reference and to our server. A request a probe sends as text is
// Latin-1, so a "é" in it is the byte 0xE9. One with "-tls" and "-http" variants runs on each port.
internal static partial class ParityProbes
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan EndWait = TimeSpan.FromSeconds(3);
    private const string ChunkedHead = "POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n";
    private const string Second = "GET /second HTTP/1.1\r\nHost: x\r\n\r\n";

    // A probe's ClientHello, captured once and replayed to both servers, by the probe's name: each capture has a
    // random and a key share of its own. Both targets present one name, the certificate's, so the SNI of whichever
    // comes first holds for the other.
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> CapturedHellos = new();

    public static IReadOnlyList<Probe> All { get; } = Requests()
        .Concat(RequestLines())
        .Concat(Headers())
        .Concat(Hosts())
        .Concat(Framing())
        .Concat(Waits())
        .Concat(Handshakes())
        .Concat(ClientRequests())
        .Concat(Port80())
        .Concat(BinaryStarts())
        .Concat(Arrivals())
        .Concat(Bulk())
        .ToArray();

    private static string Host(ProbeTarget target) => target.Sni;

    private static string Browser(ProbeTarget target, string method, string path) =>
        $"{method} {path} HTTP/1.1\r\nHost: {Host(target)}\r\n" +
        "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36\r\n" +
        "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\r\n" +
        "Accept-Language: en-US,en;q=0.9\r\nAccept-Encoding: gzip, deflate, br\r\nConnection: keep-alive\r\n\r\n";

    private static string Simple(ProbeTarget target, string method, string path, string extra = "") =>
        $"{method} {path} HTTP/1.1\r\nHost: {Host(target)}\r\n{extra}\r\n";

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);

    private static string Get(string host) => $"GET / HTTP/1.1\r\nHost: {host}\r\n\r\n";

    private static string PostChunked(string transferEncoding, string body = "0\r\n\r\n" + Second) =>
        $"POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: {transferEncoding}\r\n\r\n{body}";

    private static string PostLength(long length, string body = "") =>
        $"POST / HTTP/1.1\r\nHost: x\r\nContent-Length: {length}\r\n\r\n{body}";

    // one request, its reply, then whether the connection stays and how it ends
    private static async Task Shot(ProbeTarget target, StringBuilder log, byte[] request, bool tls, TimeSpan endWait,
        CancellationToken cancellationToken, bool bufferWrites = false, bool timeReplies = false)
    {
        await using var connection = await ProbeConnection.Open(tls ? target.Tls : target.Http, tls, target.Sni, log,
            cancellationToken, bufferWrites: bufferWrites, timeReplies: timeReplies);
        await connection.Send(request, ProbeConnection.Escape(Encoding.Latin1.GetString(request)), cancellationToken);
        await connection.Read(Quiet, Wait, cancellationToken);
        await connection.WaitEnd(endWait, cancellationToken);
    }

    private static Probe Tls(string name, string request, ProbeKind kind = ProbeKind.Quick, int endWaitSeconds = 3) =>
        new(name, kind, (target, log, cancellationToken) =>
            Shot(target, log, Latin1(request), tls: true, TimeSpan.FromSeconds(endWaitSeconds), cancellationToken));

    private static Probe Http(string name, string request, ProbeKind kind = ProbeKind.Quick, int endWaitSeconds = 3) =>
        new(name, kind, (target, log, cancellationToken) =>
            Shot(target, log, Latin1(request), tls: false, TimeSpan.FromSeconds(endWaitSeconds), cancellationToken));

    private static Probe Tls(string name, Func<ProbeTarget, string> request) =>
        new(name, ProbeKind.Quick, (target, log, cancellationToken) =>
            Shot(target, log, Latin1(request(target)), tls: true, EndWait, cancellationToken));

    private static Probe Http(string name, Func<ProbeTarget, string> request) =>
        new(name, ProbeKind.Quick, (target, log, cancellationToken) =>
            Shot(target, log, Latin1(request(target)), tls: false, EndWait, cancellationToken));

    // the request on each port, as "-tls" and "-http"
    private static IEnumerable<Probe> Both(string name, string request, ProbeKind kind = ProbeKind.Quick,
        int endWaitSeconds = 3)
    {
        yield return Tls($"{name}-tls", request, kind, endWaitSeconds);
        yield return Http($"{name}-http", request, kind, endWaitSeconds);
    }

    // a request too long for one write: encrypted as a whole first, and a probe of its own on each port. Its replies
    // say when they came, as a drain that slows shows only there; it runs alone, so their time is the server's
    private static IEnumerable<Probe> BothBulk(string name, Func<string> request, int endWaitSeconds)
    {
        foreach (var tls in new[] { true, false })
            yield return new Probe($"{name}-{(tls ? "tls" : "http")}", ProbeKind.Bulk, (target, log, cancellationToken) =>
                Shot(target, log, Latin1(request()), tls, TimeSpan.FromSeconds(endWaitSeconds), cancellationToken,
                    bufferWrites: true, timeReplies: true));
    }

    // Requests that a long tail follows: with that tail still on its way, Windows may report either a close or a
    // reset even for Kestrel. So this pins only that the rejection arrives and the connection ends promptly; the
    // ordinary probes pin how a connection ends.
    private static IEnumerable<Probe> RejectionWithTail(string name, string request)
    {
        foreach (var tls in new[] { true, false })
            yield return new Probe($"{name}-{(tls ? "tls" : "http")}", ProbeKind.Quick, async (target, log, cancellationToken) => {
                var observation = new StringBuilder();
                await using var connection = await ProbeConnection.Open(tls ? target.Tls : target.Http, tls,
                    target.Sni, observation, cancellationToken, bufferWrites: true);
                await connection.Send(Latin1(request), name, cancellationToken);
                await connection.Read(Quiet, Wait, cancellationToken);
                await connection.WaitEnd(EndWait, cancellationToken);
                log.AppendLine($"> {name}");
                var lines = observation.ToString().Split('\n');
                log.AppendLine(lines.FirstOrDefault(x => x.StartsWith("< HTTP/", StringComparison.Ordinal))?.TrimEnd('\r')
                               ?? "< no reply");
                log.AppendLine(lines.Any(x => x.StartsWith("= open", StringComparison.Ordinal)) ? "= open" : "= ended");
            });
    }

    // The parts with their pauses, on each port as "-tls" and "-http", then the reply and how the connection ends.
    // What came before the last part shows before it, so a reply to a request not yet whole tells; only there, as
    // a reply the parts before it draw may still be on its way at a shorter pause.
    private static IEnumerable<Probe> BothPieces(string name, IReadOnlyList<(string Text, int DelayMs)> parts,
        ProbeKind kind = ProbeKind.Quick, int endWaitSeconds = 9)
    {
        foreach (var tls in new[] { true, false })
            yield return new Probe($"{name}-{(tls ? "tls" : "http")}", kind, async (target, log, cancellationToken) => {
                await using var connection = await ProbeConnection.Open(tls ? target.Tls : target.Http, tls,
                    target.Sni, log, cancellationToken);
                for (var i = 0; i < parts.Count; i++) {
                    if (i == parts.Count - 1)
                        await connection.Read(TimeSpan.Zero, TimeSpan.Zero, cancellationToken);
                    await connection.Send(parts[i].Text, cancellationToken);
                    await Task.Delay(parts[i].DelayMs, cancellationToken);
                }
                await connection.Read(Quiet, Wait, cancellationToken);
                await connection.WaitEnd(TimeSpan.FromSeconds(endWaitSeconds), cancellationToken);
            });
    }

    // a byte at a time, each after a pause
    private static IReadOnlyList<(string Text, int DelayMs)> OneByOne(string text, int delayMs) =>
        text.Select(x => (x.ToString(), delayMs)).ToArray();

    private static Probe Hello(string name, Func<ProbeTarget, string?> sni, IReadOnlyList<SslApplicationProtocol>? alpn,
        SslProtocols protocols)
    {
        return new Probe(name, ProbeKind.Quick, async (target, log, cancellationToken) => {
            var capture = CapturedHellos.GetOrAdd(name, _ => new Lazy<Task<byte[]>>(() =>
                TlsHandshake.CaptureClientHello(sni(target), alpn, protocols, CancellationToken.None)));
            var clientHello = await capture.Value.WaitAsync(cancellationToken);
            log.AppendLine($"> ClientHello {protocols} sni={sni(target) ?? "-"} " +
                           $"alpn={(alpn == null ? "-" : string.Join(",", alpn))}");
            log.AppendLine("< " + await TlsHandshake.Replay(target.Tls, clientHello, cancellationToken));
        });
    }
}

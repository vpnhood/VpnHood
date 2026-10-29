using System.Text;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // Kestrel's timeouts: 130 s of keep-alive before a request starts, 30 s for its headers, 5 s to drain a body the
    // reply left unread, and a TLS handshake's 10 s; each fires on its one-second heartbeat
    private static IReadOnlyList<Probe> Waits() => [
        new("idle", ProbeKind.Slow, async (target, log, cancellationToken) => {
            await using var connection = await OpenTls(target, log, cancellationToken);
            log.AppendLine("> (nothing)");
            await connection.WaitEnd(TimeSpan.FromSeconds(150), cancellationToken);
        }),
        new("idle-after-401", ProbeKind.Slow, async (target, log, cancellationToken) => {
            await using var connection = await OpenTls(target, log, cancellationToken);
            await connection.Send(Simple(target, "GET", "/"), cancellationToken);
            await connection.Read(Quiet, Wait, cancellationToken);
            await connection.WaitEnd(TimeSpan.FromSeconds(150), cancellationToken);
        }),
        .. Both("leading-crlf-idle", "\r\n", ProbeKind.Slow, endWaitSeconds: 150),
        new("partial-request", ProbeKind.Slow, async (target, log, cancellationToken) => {
            await using var connection = await OpenTls(target, log, cancellationToken);
            await connection.Send("GET / HTTP/1.1\r\nHost: x\r\n", cancellationToken);
            await connection.WaitEnd(TimeSpan.FromSeconds(45), cancellationToken);
        }),
        new("partial-after-401", ProbeKind.Slow, async (target, log, cancellationToken) => {
            await using var connection = await OpenTls(target, log, cancellationToken);
            await connection.Send(Simple(target, "GET", "/"), cancellationToken);
            await connection.Read(Quiet, Wait, cancellationToken);
            await connection.Send("GET / HTTP/1.1\r\n", cancellationToken);
            await connection.WaitEnd(TimeSpan.FromSeconds(45), cancellationToken);
        }),
        .. Both("header-tls-start", "GET / HTTP/1.1\r\nHost: x\r\n\u0016\u0003", ProbeKind.Slow, endWaitSeconds: 40),
        new("post-no-body", ProbeKind.Slow, async (target, log, cancellationToken) => {
            await using var connection = await OpenTls(target, log, cancellationToken);
            await connection.Send(Simple(target, "POST", "/api", "Content-Length: 100\r\n"), cancellationToken);
            await connection.Read(Quiet, Wait, cancellationToken);
            await connection.WaitEnd(TimeSpan.FromSeconds(45), cancellationToken);
        }),
        new("slow-body", ProbeKind.Slow, async (target, log, cancellationToken) => {
            await using var connection = await OpenTls(target, log, cancellationToken);
            await connection.Send(Simple(target, "POST", "/api", "Content-Length: 3000\r\n"), cancellationToken);
            await connection.Read(Quiet, Wait, cancellationToken);
            // a body that ends within Kestrel's 5 s drain keeps the connection for the next request
            for (var i = 0; i < 3; i++) {
                await Task.Delay(800, cancellationToken);
                await connection.Send(Latin1(new string('a', 1000)), "1000 bytes", cancellationToken);
            }
            await connection.Send(Simple(target, "GET", "/"), cancellationToken);
            await connection.Read(Quiet, Wait, cancellationToken);
            await connection.WaitEnd(EndWait, cancellationToken);
        }),
        Tls("post-huge-length", t => Simple(t, "POST", "/api", "Content-Length: 100000000\r\n") + new string('a', 1000)),
        new("tls-no-hello", ProbeKind.Slow, async (target, log, cancellationToken) => {
            await using var connection = await ProbeConnection.Open(target.Tls, false, null, log, cancellationToken);
            log.AppendLine("> (TCP only, no ClientHello)");
            await connection.WaitEnd(TimeSpan.FromSeconds(40), cancellationToken);
        })
    ];

    private static Task<ProbeConnection> OpenTls(ProbeTarget target, StringBuilder log,
        CancellationToken cancellationToken)
    {
        return ProbeConnection.Open(target.Tls, true, target.Sni, log, cancellationToken);
    }
}

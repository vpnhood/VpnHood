using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // the TLS handshake, replayed from fixed ClientHellos: versions, ALPN and SNI, and what isn't a handshake
    private static IReadOnlyList<Probe> Handshakes() => [
        Hello("tls13-sni", target => target.Sni, null, SslProtocols.Tls13),
        Hello("tls13-nosni", _ => null, null, SslProtocols.Tls13),
        Hello("tls13-othersni", _ => "www.example.com", null, SslProtocols.Tls13),
        Hello("tls12-sni", target => target.Sni, null, SslProtocols.Tls12),
        Hello("tls12-alpn-h2-h11", target => target.Sni, [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11],
            SslProtocols.Tls12),
        Hello("tls12-alpn-h2-only", target => target.Sni, [SslApplicationProtocol.Http2], SslProtocols.Tls12),
        new("tls-plain-http", ProbeKind.Quick, async (target, log, cancellationToken) => {
            log.AppendLine("> plain GET to the TLS port");
            var request = Encoding.ASCII.GetBytes(Simple(target, "GET", "/"));
            log.AppendLine("< " + await TlsHandshake.Replay(target.Tls, request, cancellationToken));
        }),
        // seeded, so both servers get the same bytes
        new("tls-garbage", ProbeKind.Quick, async (target, log, cancellationToken) => {
            log.AppendLine("> 64 seeded random bytes in place of a ClientHello");
            log.AppendLine("< " + await TlsHandshake.Replay(target.Tls, Fixed(64, withLf: false), cancellationToken));
        })
    ];
}

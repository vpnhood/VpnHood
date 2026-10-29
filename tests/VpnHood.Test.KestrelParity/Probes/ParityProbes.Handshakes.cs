using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // the TLS handshake, replayed from fixed ClientHellos: versions, ALPN and SNI, and what isn't a handshake
    private static IReadOnlyList<Probe> Handshakes() => [
        new("tls13-sni", ProbeKind.Quick, (target, log, cancellationToken) =>
            Hello(target, log, target.Sni, null, SslProtocols.Tls13, cancellationToken)),
        new("tls13-nosni", ProbeKind.Quick, (target, log, cancellationToken) =>
            Hello(target, log, null, null, SslProtocols.Tls13, cancellationToken)),
        new("tls13-othersni", ProbeKind.Quick, (target, log, cancellationToken) =>
            Hello(target, log, "www.example.com", null, SslProtocols.Tls13, cancellationToken)),
        new("tls12-sni", ProbeKind.Quick, (target, log, cancellationToken) =>
            Hello(target, log, target.Sni, null, SslProtocols.Tls12, cancellationToken)),
        new("tls12-alpn-h2-h11", ProbeKind.Quick, (target, log, cancellationToken) =>
            Hello(target, log, target.Sni, [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11],
                SslProtocols.Tls12, cancellationToken)),
        new("tls12-alpn-h2-only", ProbeKind.Quick, (target, log, cancellationToken) =>
            Hello(target, log, target.Sni, [SslApplicationProtocol.Http2], SslProtocols.Tls12, cancellationToken)),
        new("tls-plain-http", ProbeKind.Quick, async (target, log, cancellationToken) => {
            log.AppendLine("> plain GET to the TLS port");
            var request = Encoding.ASCII.GetBytes(Simple(target, "GET", "/"));
            log.AppendLine("< " + await TlsHandshake.Replay(target.Tls, request, cancellationToken));
        }),
        new("tls-garbage", ProbeKind.Quick, async (target, log, cancellationToken) => {
            log.AppendLine("> 64 random bytes in place of a ClientHello");
            log.AppendLine("< " + await TlsHandshake.Replay(target.Tls, RandomNumberGenerator.GetBytes(64), cancellationToken));
        })
    ];
}

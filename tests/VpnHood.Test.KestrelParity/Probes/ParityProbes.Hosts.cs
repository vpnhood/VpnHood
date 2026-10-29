using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // the Host header: Kestrel checks its characters only, as Http.Sys does; a port needs a digit, and brackets
    // hold at least three hex digits, colons or dots without being an address
    private static IReadOnlyList<Probe> Hosts() => [
        Tls("bad-host", "GET / HTTP/1.1\r\nHost: a b\r\n\r\n"),
        Tls("bad-host-port", "GET / HTTP/1.1\r\nHost: a:x\r\n\r\n"),
        Tls("host-slash", "GET / HTTP/1.1\r\nHost: a/b\r\n\r\n"),
        Tls("host-ipv6", "GET / HTTP/1.1\r\nHost: [::1]:443\r\n\r\n"),
        Tls("host-empty", "GET / HTTP/1.1\r\nHost:\r\n\r\n"),
        .. Both("host-star", Get("a*b")),
        .. Both("host-plus", Get("a+b")),
        .. Both("host-percent", Get("a%41")),
        .. Both("host-underscore", Get("a_b")),
        .. Both("host-comma", Get("a,b")),
        .. Both("host-semicolon", Get("a;b")),
        .. Both("host-equals", Get("a=b")),
        .. Both("host-port", Get("a:80")),
        .. Both("host-colon-empty-port", Get("a:")),
        .. Both("host-port-only", Get(":80")),
        .. Both("host-colon-only", Get(":")),
        .. Both("host-v6-loopback", Get("[::1]")),
        .. Both("host-v6-any", Get("[::]")),
        .. Both("host-v6-empty-port", Get("[::1]:")),
        .. Both("host-bracket-suffix", Get("[::1]:garbage:443")),
        .. Both("host-bracket-ipv4", Get("[1.2.3.4]")),
        .. Both("host-bracket-1", Get("[1]")),
        .. Both("host-bracket-abc", Get("[abc]")),
        .. Both("host-bracket-0x1", Get("[0x1]")),
        .. Both("host-bracket-scope", Get("[::1%x]")),
        .. Both("host-bracket-dots", Get("[...]"))
    ];
}

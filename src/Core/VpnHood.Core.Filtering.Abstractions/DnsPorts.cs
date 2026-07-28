namespace VpnHood.Core.Filtering.Abstractions;

// The destination ports that identify DNS traffic. A filter stage sees only (protocol, endpoint), so the
// port IS the whole DNS signal available to it — and it is deliberately protocol-agnostic: 53 is plain DNS
// over both udp and tcp, 853 is DNS-over-TLS (tcp) and its quic variant. DoH is absent on purpose: on 443
// it is indistinguishable from any other https flow, so no port test can find it.
public static class DnsPorts
{
    public const int Dns = 53;
    public const int DnsOverTls = 853;

    // True for every real DNS transport pair: 53 carries DNS over both udp (classic) and tcp (truncation
    // fallback), 853 carries DoT over tcp and DoQ (RFC 9250) over udp — a protocol check would add nothing
    // except the temptation to narrow this to udp/53 + tcp/853 and thereby miss tcp-fallback DNS and DoQ.
    public static bool IsDnsPort(int port) => port is Dns or DnsOverTls;
}

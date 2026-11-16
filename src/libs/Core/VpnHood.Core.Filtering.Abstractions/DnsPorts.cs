using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

// The destination ports that identify DNS traffic. A filter stage sees only (protocol, endpoint), so the
// port IS the whole DNS signal available to it — and it is deliberately protocol-agnostic: 53 is plain DNS
// over both udp and tcp, 853 is DNS-over-TLS (tcp) and its quic variant. DoH is absent on purpose: on 443
// it is indistinguishable from any other https flow, so no port test can find it.
/// <summary>
/// Provides the destination ports that tell DNS flows to a filter, which sees only a flow's protocol and destination.
/// </summary>
/// <remarks>
/// DNS over HTTPS has no port of its own: on port 443, no port test can tell it from any other HTTPS flow.
/// </remarks>
public static class DnsPorts
{
    /// <summary>The port of plain DNS, over UDP and TCP: 53.</summary>
    public const int Dns = 53;

    /// <summary>The port of DNS over TLS, on TCP, and of DNS over QUIC, on UDP: 853.</summary>
    public const int DnsOverTls = 853;

    /// <summary>Determines whether a port is one of DNS's, over UDP or TCP.</summary>
    /// <param name="port">The destination port.</param>
    /// <returns><see langword="true"/> if the port is 53 or 853; otherwise, <see langword="false"/>.</returns>
    // No protocol check: one would invite narrowing it to udp/53 and tcp/853, which misses TCP's fallback and DoQ
    public static bool IsDnsPort(int port) => port is Dns or DnsOverTls;

    /// <summary>
    /// Determines whether a flow is secure DNS: DNS over TLS, on TCP port 853, or DNS over QUIC (RFC 9250), on UDP
    /// port 853.
    /// </summary>
    /// <param name="protocol">The flow's protocol.</param>
    /// <param name="port">The destination port.</param>
    /// <returns>
    /// <see langword="true"/> if the flow is TCP or UDP to port 853; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool IsSecureDns(IpProtocol protocol, int port) =>
        protocol is IpProtocol.Tcp or IpProtocol.Udp && port is DnsOverTls;
}

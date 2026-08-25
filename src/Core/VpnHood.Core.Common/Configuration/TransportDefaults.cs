using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Common.Configuration;

/// <summary>
/// What both sides of the transport share: the fixed values a client and a server must agree on,
/// and the fallbacks for knobs nobody supplied. Anything only one side reads lives with that side
/// instead — <c>ClientTransportOptions</c> and <c>ServerTransportDefaults</c>.
/// </summary>
public static class TransportDefaults
{
    public const int MaxPacketSize = 1500;
    public const int MtuOverhead = 60 + 20 + 40; // 60 for ip header + 20 for (TCP or UDP) + 40 for session header
    public const int MtuSafety = 100;
    public const int MtuServer = MaxPacketSize;
    public const int MtuClient = MaxPacketSize - MtuSafety;
    public const string HttpPassCheck = "VpnHoodPassCheck";
    public const int MaxUdpDatagramSize = 64 * 1024;

    // Covers any response a real-world resolver exchanges over UDP (post-DNS-Flag-Day defaults are
    // ~1232 bytes; 4096 is the common EDNS ceiling, though the protocol permits more). A rare larger
    // reply is detected via MSG_TRUNC and dropped like packet loss — see UdpProxy.
    public const int UdpDnsBufferSize = 4 * 1024;
    public const int MaxPacketChannelCount = 8;
    public const int StreamSmallReadCacheSize = 512;
    public const int ProxyPacketQueueCapacity = 200;
    public const int TunnelPacketQueueCapacity = 200;
    public const int MaxUdpClientCount = 100;
    // DNS workers are small (4 KB) and recycle every UdpDnsTimeout, so a session needs far fewer
    // of them than general UDP workers
    public const int MaxUdpDnsClientCount = 70;
    public const int MaxPingClientCount = 10;
    public const int PrefetchStreamBufferSize = 1024 * 4;

    public static TransferBufferSize ConnectionPacketBufferSize { get; } =
        new(0xFFFF * 4, 0xFFFF * 4); // 256KB send, 256KB receive

    public static TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public static TimeSpan UdpTimeout { get; set; } = TimeSpan.FromMinutes(2);
    // DNS is one request/response round trip; holding its mapping for the full UdpTimeout lets a DNS
    // burst pin every pool slot (each concurrent query from a distinct source port to the same server
    // needs its own worker). DNS flows therefore run on segregated short-lived workers.
    public static TimeSpan UdpDnsTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public static TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromMinutes(1); // it is for worker timeout
    public static TimeSpan TcpCheckInterval { get; set; } = TimeSpan.FromMinutes(15);
    public static TimeSpan TcpGracefulTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public static TimeSpan ByeTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
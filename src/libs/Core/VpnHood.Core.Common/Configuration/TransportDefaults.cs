using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Common.Configuration;

/// <summary>
/// Provides what both sides of the transport share: the values a client and a server must agree on, and the defaults
/// of settings nobody gave.
/// </summary>
/// <remarks>
/// What one side alone reads is that side's: <c>ClientTransportOptions</c> and <see cref="ServerTransportDefaults"/>.
/// The settable values are the process's, read when they are needed.
/// </remarks>
public static class TransportDefaults
{
    /// <summary>The largest packet on a path of the internet's usual MTU: 1,500 bytes.</summary>
    public const int MaxPacketSize = 1500;

    /// <summary>
    /// The bytes taken off the smaller MTU of a client and a server before protocol 16: 120, for the headers around a
    /// packet in the tunnel.
    /// </summary>
    // Compatibility (2026-11): a server before HelloResponse.TunnelMtu carries packets up to the smaller MTU less this;
    // drop once VpnHoodClientConfig.MinProtocolVersion is above 15
    public const int MtuOverhead = 60 + 20 + 40;

    /// <summary>The bytes a client's MTU leaves below <see cref="MaxPacketSize"/>: 100.</summary>
    public const int MtuSafety = 100;

    /// <summary>The MTU a server gives its interface: 1,500 bytes.</summary>
    public const int MtuServer = MaxPacketSize;

    /// <summary>
    /// The largest packet a tunnel carries, both ways, which a client asks for and a server holds a session to: 1,400
    /// bytes, so the UDP channel's datagram for it, with its IPv6 and UDP headers, fits a 1,500-byte path.
    /// </summary>
    // Steam's games send UDP payloads of up to 1300 bytes, 1328-byte packets
    public const int MtuClient = MaxPacketSize - MtuSafety;

    /// <summary>The largest UDP datagram the forwarders take: 64 KB.</summary>
    public const int MaxUdpDatagramSize = 64 * 1024;

    /// <summary>
    /// The buffer of a forwarded DNS exchange over UDP: 4 KB, the common EDNS ceiling; a larger reply is dropped, as a
    /// lost packet.
    /// </summary>
    // post-DNS-Flag-Day defaults are ~1232 bytes; a larger reply is detected via MSG_TRUNC — see UdpForwarder
    public const int UdpDnsBufferSize = 4 * 1024;

    /// <summary>The default most of a client's packet channels: 8.</summary>
    public const int MaxPacketChannelCount = 8;

    /// <summary>The read buffer of a tunnel stream's HTTP or WebSocket framing: 512 bytes.</summary>
    public const int StreamSmallReadCacheSize = 512;

    /// <summary>The packets a forwarding queue holds: 200.</summary>
    public const int PacketForwardingQueueCapacity = 200;

    /// <summary>The default most of a session's UDP forwarding workers: 100.</summary>
    public const int MaxUdpClientCount = 100;

    /// <summary>
    /// The most of a session's DNS forwarding workers: 70, small ones, each removed once unused for
    /// <see cref="UdpDnsTimeout"/>.
    /// </summary>
    public const int MaxUdpDnsClientCount = 70;

    /// <summary>The most of a client session's ping workers: 10; a server's session options have their own default.</summary>
    public const int MaxPingClientCount = 10;

    /// <summary>The first read of a TCP connection the client forwards, as for its TLS ClientHello: 4 KB.</summary>
    public const int PrefetchStreamBufferSize = 1024 * 4;

    /// <summary>
    /// Gets the default buffer sizes of a packet channel's connection: about 256 KB (262,140 bytes) each way.
    /// </summary>
    public static TransferBufferSize ConnectionPacketBufferSize { get; } =
        new(0xFFFF * 4, 0xFFFF * 4); // 256KB send, 256KB receive

    /// <summary>Gets or sets how long a ping waits for its reply. The default is 5 seconds.</summary>
    public static TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how long a UDP flow's forwarding lasts unused. The default is 2 minutes.</summary>
    public static TimeSpan UdpTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets how long a DNS flow's forwarding lasts unused, shorter than <see cref="UdpTimeout"/>, so a burst of
    /// queries does not hold every worker. The default is 10 seconds.
    /// </summary>
    public static TimeSpan UdpDnsTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets how long a session's ping destination stays remembered unused. The default is 1 minute.</summary>
    public static TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets how often forwarded TCP connections are checked. The default is 15 minutes.</summary>
    public static TimeSpan TcpCheckInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets how long a tunnel stream's graceful close may take. The default is 15 seconds.</summary>
    public static TimeSpan TcpGracefulTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets how long a client's goodbye to the server may take. The default is 2 seconds.</summary>
    public static TimeSpan ByeTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the bytes a speed-limited session may move at full speed after a pause, each way, as cable
    /// providers allow a burst. The default is 10 MB.
    /// </summary>
    /// <remarks>
    /// Short use, such as a page load, runs at full speed, and heavy use is held to the limit. The burst has no peak
    /// rate, so a heavy transfer may stall once as it meets the limit, for as long as its debt takes to pay, and each
    /// new session starts with a full burst.
    /// </remarks>
    public static long MaxSpeedBurst { get; set; } = VhUtils.FromMB(10);
}
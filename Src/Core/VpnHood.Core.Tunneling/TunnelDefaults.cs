using System.Net;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling;

public static class TunnelDefaults
{
    public const int MaxPacketChannelCount = 8;
    public const int MtuOverhead = 70;
    public const int Mtu = 1500 - MtuOverhead;
    public const int MaxPacketSize = 1500;
    public const string HttpPassCheck = "VpnHoodPassCheck";
    public const int ClientStreamBufferSize = 0xFFFF * 2; // 128KB
    public const int ServerStreamBufferSize = 0xFFFF / 4; // 16KB
    public const int StreamSmallReadCacheSize = 512;
    public const int StreamProxyBufferSize = 0x1000 * 2; // 8KB
    public const int ClientUdpChannelReceiveBufferSize = 1024 * 1024 * 1; // 1MB
    public const int ClientUdpChannelUdpSendBufferSize = 1024 * 1024 * 1; // 1MB
    public const int ClientUdpProxyReceiveBufferSize = 1024 * 1024 * 1; // 1MB
    public const int ClientUdpProxySendBufferSize = 1024 * 1024 * 1; // 1MB
    public const int ProxyPacketQueueCapacity = 200;
    public const int TunnelPacketQueueCapacity = 200;
    public const int MaxUdpClientCount = 500;
    public const int MaxPingClientCount = 10;
    public const int StreamPacketReaderBufferSize = 1500 * 50; // 50KB about 50 packets

    public static int? ServerUdpProxyReceiveBufferSize { get; set; } = null; // system default
    public static int? ServerUdpProxySendBufferSize { get; set; } = null; // system default
    public static int? ServerUdpChannelReceiveBufferSize { get; set; } = null; // system default
    public static int? ServerUdpChannelSendBufferSize { get; set; } = null; // system default

    public static TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public static TimeSpan UdpTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public static TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromMinutes(1); // it is for worker timeout
    public static TimeSpan TcpCheckInterval { get; set; } = TimeSpan.FromMinutes(15);
    public static TimeSpan TcpGracefulTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public static TimeSpan ByeTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public static TimeSpan ClientRequestTimeoutDelta { get; set; } = TimeSpan.FromSeconds(10);
    public static IpNetwork VirtualIpNetworkV4 { get; } = new(IPAddress.Parse("10.240.0.1"), 12); //1M (enough for reservation)
    public static IpNetwork VirtualIpNetworkV6 { get; } = new(IPAddress.Parse("fd12:2020::1"), 48);

}
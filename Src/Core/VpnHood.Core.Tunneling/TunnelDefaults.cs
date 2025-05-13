using System.Net;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling;

public static class TunnelDefaults
{
    public const int MaxDatagramChannelCount = 8;
    public const int MtuOverhead = 70;
    public const int Mtu = 1500 - MtuOverhead;
    public const int MtuRemote = 1500;
    public const string HttpPassCheck = "VpnHoodPassCheck";
    public const int StreamProxyBufferSize = 0x1000 * 2;
    public const int ClientUdpReceiveBufferSize = 1024 * 1024 * 1; // 1MB
    public const int ClientUdpSendBufferSize = 1024 * 1024 * 1; // 1MB
    public const int ProxyPacketQueueCapacity = 200;
    public const int TunnelPacketQueueCapacity = 200;
    public const int MaxUdpClientCount = 500;
    public const int MaxPingClientCount = 10;

    public static int? ServerUdpReceiveBufferSize { get; set; } = null; // system default
    public static int? ServerUdpSendBufferSize { get; set; } = null; // system default
    public static TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public static TimeSpan UdpTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public static TimeSpan IcmpTimeout { get; set; } = TimeSpan.FromMinutes(1); // it is for worker timeout
    public static TimeSpan TcpCheckInterval { get; set; } = TimeSpan.FromMinutes(15);
    public static TimeSpan TcpGracefulTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public static TimeSpan ClientRequestTimeoutDelta { get; set; } = TimeSpan.FromSeconds(10);
    public static IpNetwork VirtualIpNetworkV4 { get; } = new(IPAddress.Parse("10.240.0.1"), 12); //1M (enough for reservation)
    public static IpNetwork VirtualIpNetworkV6 { get; } = new(IPAddress.Parse("fd12:2020::1"), 48);

}
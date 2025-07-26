using System.Net;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling;

public static class TunnelDefaults
{
    public const int MaxPacketChannelCount = 8;
    public const int MtuOverhead = 70;
    public const int Mtu = 1500 - MtuOverhead;
    public const int MaxPacketSize = 1500;
    public const string HttpPassCheck = "VpnHoodPassCheck";
    public const int StreamSmallReadCacheSize = 512;
    public const int ProxyPacketQueueCapacity = 200;
    public const int TunnelPacketQueueCapacity = 200;
    public const int MaxUdpClientCount = 500;
    public const int MaxPingClientCount = 10;
    public static TransferBufferSize ClientStreamProxyBufferSize { get; } = new(0xFFFF / 8, 0xFFFF / 8); // 8KB send, 8KB receive
    public static TransferBufferSize ServerStreamProxyBufferSize { get; } = new(0xFFFF / 8, 0xFFFF / 8); // 8KB send, 8KB receive
    
    public static TransferBufferSize? ClientUdpProxyBufferSize { get; set; } = new(1024 * 1024 * 1, 1024 * 1024 * 1); // 1MB send, 1MB receive
    public static TransferBufferSize? ServerUdpProxyBufferSize { get; set; } = null; // system default

    public static TransferBufferSize ClientStreamPacketBufferSize { get; } = new(0xFFFF * 4, 0xFFFF * 4); // 256KB send, 256KB receive
    public static TransferBufferSize ServerStreamPacketBufferSize { get; } = new(0xFFFF / 4, 0xFFFF / 4); // 16KB send, 16KB receive
    
    public static TransferBufferSize? ClientUdpChannelBufferSize { get; set; } = new(1024 * 1024 * 1, 1024 * 1024 * 1); // 1MB send, 1MB receive
    public static TransferBufferSize? ServerUdpChannelBufferSize { get; set; } = null; // system default

    public static TransferBufferSize? ServerTcpKernelBufferSize { get; set; } = null; // system default
    
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
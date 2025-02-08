using System.Net;
using VpnHood.Core.Common.Net;

namespace VpnHood.Core.Tunneling;

public static class TunnelDefaults
{
    public const int MaxDatagramChannelCount = 8;
    public const int TlsHandshakeLength = 5000;
    public const int MtuWithFragmentation = 0xFFFF - 70;
    public const int MtuWithoutFragmentation = 1500 - 70;
    public const string HttpPassCheck = "VpnHoodPassCheck";
    public const int StreamProxyBufferSize = 0x1000 * 2;
    public static TimeSpan TcpCheckInterval { get; set; } = TimeSpan.FromMinutes(15);
    public static TimeSpan TcpGracefulTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public static TimeSpan ClientRequestTimeoutDelta { get; set; } = TimeSpan.FromSeconds(10);
    public static IpNetwork VirtualIpNetworkV4 { get; } = new(IPAddress.Parse("10.255.0.0"), 16);
    public static IpNetwork VirtualIpNetworkV6 { get; } = new(IPAddress.Parse("fd12:2020::"), 48);
}
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Net.Extensions;

public static class NetExtensions
{
    public static IpVersion IpVersion(this IPEndPoint ipEndPoint)
        => ipEndPoint.AddressFamily.IpVersion();

    public static IpVersion IpVersion(this IPAddress ipAddress)
        => ipAddress.AddressFamily.IpVersion();

    public static IpVersion IpVersion(this AddressFamily addressFamily)
    {
        return addressFamily switch {
            System.Net.Sockets.AddressFamily.InterNetwork => Net.IpVersion.IPv4,
            System.Net.Sockets.AddressFamily.InterNetworkV6 => Net.IpVersion.IPv6,
            _ => throw new NotSupportedException("Unsupported address family.")
        };
    }

    public static AddressFamily AddressFamily(this IpVersion ipVersion)
    {
        return ipVersion switch {
            Net.IpVersion.IPv4 => System.Net.Sockets.AddressFamily.InterNetwork,
            Net.IpVersion.IPv6 => System.Net.Sockets.AddressFamily.InterNetworkV6,
            _ => throw new NotSupportedException("Unsupported IP version.")
        };
    }
}
using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets.Extensions;

/// <summary>
/// Provides conversions between .NET's address families and <see cref="Toolkit.Net.IpVersion"/>.
/// </summary>
public static class NetExtensions
{
    /// <summary>
    /// Gets the IP version of an endpoint's address.
    /// </summary>
    /// <param name="ipEndPoint">The endpoint.</param>
    /// <returns><see cref="Toolkit.Net.IpVersion.IPv4"/> or <see cref="Toolkit.Net.IpVersion.IPv6"/>.</returns>
    /// <exception cref="NotSupportedException">The address is neither IPv4 nor IPv6.</exception>
    public static IpVersion IpVersion(this IPEndPoint ipEndPoint)
        => ipEndPoint.AddressFamily.IpVersion();

    /// <summary>
    /// Gets the IP version of an address.
    /// </summary>
    /// <param name="ipAddress">The address.</param>
    /// <returns><see cref="Toolkit.Net.IpVersion.IPv4"/> or <see cref="Toolkit.Net.IpVersion.IPv6"/>.</returns>
    /// <exception cref="NotSupportedException">The address is neither IPv4 nor IPv6.</exception>
    public static IpVersion IpVersion(this IPAddress ipAddress)
        => ipAddress.AddressFamily.IpVersion();

    /// <summary>
    /// Gets the IP version of an address family.
    /// </summary>
    /// <param name="addressFamily">The address family.</param>
    /// <returns><see cref="Toolkit.Net.IpVersion.IPv4"/> or <see cref="Toolkit.Net.IpVersion.IPv6"/>.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="addressFamily"/> is neither <see cref="System.Net.Sockets.AddressFamily.InterNetwork"/> nor
    /// <see cref="System.Net.Sockets.AddressFamily.InterNetworkV6"/>.
    /// </exception>
    public static IpVersion IpVersion(this AddressFamily addressFamily)
    {
        return addressFamily switch {
            System.Net.Sockets.AddressFamily.InterNetwork => Toolkit.Net.IpVersion.IPv4,
            System.Net.Sockets.AddressFamily.InterNetworkV6 => Toolkit.Net.IpVersion.IPv6,
            _ => throw new NotSupportedException("Unsupported address family.")
        };
    }

    /// <summary>
    /// Gets the address family of an IP version.
    /// </summary>
    /// <param name="ipVersion">The IP version.</param>
    /// <returns>
    /// <see cref="System.Net.Sockets.AddressFamily.InterNetwork"/> or
    /// <see cref="System.Net.Sockets.AddressFamily.InterNetworkV6"/>.
    /// </returns>
    /// <exception cref="NotSupportedException"><paramref name="ipVersion"/> is neither IPv4 nor IPv6.</exception>
    public static AddressFamily AddressFamily(this IpVersion ipVersion)
    {
        return ipVersion switch {
            Toolkit.Net.IpVersion.IPv4 => System.Net.Sockets.AddressFamily.InterNetwork,
            Toolkit.Net.IpVersion.IPv6 => System.Net.Sockets.AddressFamily.InterNetworkV6,
            _ => throw new NotSupportedException("Unsupported IP version.")
        };
    }
}

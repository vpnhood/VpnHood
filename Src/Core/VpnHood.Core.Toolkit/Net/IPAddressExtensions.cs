using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Net;

// ReSharper disable once InconsistentNaming
public static class IPAddressExtensions
{
    public static bool IsV4(this IPAddress ipAddress)
    {
        return ipAddress.AddressFamily == AddressFamily.InterNetwork;
    }

    public static bool IsV6(this IPAddress ipAddress)
    {
        return ipAddress.AddressFamily == AddressFamily.InterNetworkV6;
    }

    public static bool IsV4(this IPEndPoint ipEndPoint)
    {
        return ipEndPoint.AddressFamily == AddressFamily.InterNetwork;
    }

    public static bool IsV6(this IPEndPoint ipEndPoint)
    {
        return ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6;
    }

    public static bool IsV4(this AddressFamily addressFamily)
    {
        return addressFamily == AddressFamily.InterNetwork;
    }


    public static bool IsV6(this AddressFamily addressFamily)
    {
        return addressFamily == AddressFamily.InterNetworkV6;
    }
}
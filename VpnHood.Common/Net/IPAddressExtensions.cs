using System.Net;
using System.Net.Sockets;

namespace VpnHood.Common.Net;

// ReSharper disable once InconsistentNaming
public static class IPAddressExtensions
{
    public static bool IsV6(this IPAddress ipAddress)
    {
        return ipAddress.AddressFamily == AddressFamily.InterNetworkV6;
    }

    public static bool IsV4(this IPAddress ipAddress)
    {
        return ipAddress.AddressFamily == AddressFamily.InterNetwork;
    }
}
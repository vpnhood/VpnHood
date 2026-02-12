using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling.Utils;

public static class TunnelUtils
{
    // conver TestNetwork IPs to loopback IPs, such as 198.18.x.x to 127.10.x.x,
    // to make sure the packets can be captured and return correctly in the test environment.
    public static IPAddress MapTestNetworkToLoopback(IPAddress ipAddress)
    {
        if (!ipAddress.IsTestNetwork())
            return ipAddress;

        if (ipAddress.IsV4()) {
            var bytes = ipAddress.GetAddressBytes();
            bytes[0] = 127;
            bytes[1] = (byte)(bytes[1] - 8);
            return new IPAddress(bytes);
        }

        if (ipAddress.IsV6())
            return IPAddress.IPv6Loopback;

        return ipAddress;
    }

    public static IPEndPoint MapTestNetworkToLoopback(IPEndPoint ipEndPoint)
    {
        if (!ipEndPoint.Address.IsTestNetwork())
            return ipEndPoint;

        if (ipEndPoint.IsV4()) {
            var bytes = ipEndPoint.Address.GetAddressBytes();
            bytes[0] = 127;
            bytes[1] = (byte)(bytes[1] - 8);
            return new IPEndPoint(new IPAddress(bytes), ipEndPoint.Port);
        }

        if (ipEndPoint.IsV6())
            return new IPEndPoint(IPAddress.IPv6Loopback, ipEndPoint.Port);

        return ipEndPoint;
    }

    public static IpPacket MapTestNetworkToLoopback(IpPacket ipPacket)
    {

    }


}

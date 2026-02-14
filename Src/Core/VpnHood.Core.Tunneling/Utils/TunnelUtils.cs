using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net.Extensions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling.Utils;

public static class TunnelUtils
{
    public const int TestPortMin = 49150;
    public const int TestPortMax = 49190;
    public static IPAddress RemoteTestIpV4 = IPAddress.Parse("198.18.11.1");
    public static IPAddress RemoteTestIpV6 = IPAddress.Parse("2001:db8::1");
    public static IPAddress LocalTestIpV4 = IPAddress.Loopback;
    public static IPAddress LocalTestIpV6 = IPAddress.IPv6Loopback;


    // convert TestNetwork IPs to loopback IPs, such as 198.18.x.x to 127.10.x.x,
    // to make sure the packets can be captured and return correctly in the test environment.
    public static IPAddress MapTestNetworkToLoopback(IPAddress ipAddress)
    {
        if (!IsRemoteTestAddress(ipAddress))
            return ipAddress;

        if (ipAddress.Equals(RemoteTestIpV4))
            return LocalTestIpV4;

        if (ipAddress.Equals(RemoteTestIpV6))
            return LocalTestIpV6;

        return ipAddress;
    }

    public static IPAddress RecoverTestNetworkFromLoopback(IPAddress ipAddress)
    {
        if (!IsLocalTestAddress(ipAddress))
            return ipAddress;

        if (ipAddress.Equals(LocalTestIpV4))
            return RemoteTestIpV4;

        if (ipAddress.Equals(LocalTestIpV6))
            return RemoteTestIpV6;

        return ipAddress;
    }


    public static IPEndPoint MapTestNetworkToLoopback(IPEndPoint ipEndPoint)
    {
        if (!IsRemoteTestAddress(ipEndPoint.Address))
            return ipEndPoint;

        if (ipEndPoint.Address.Equals(RemoteTestIpV4))
            return new IPEndPoint(LocalTestIpV4, ipEndPoint.Port);

        if (ipEndPoint.Address.Equals(RemoteTestIpV6))
            return new IPEndPoint(LocalTestIpV6, ipEndPoint.Port);

        return ipEndPoint;
    }

    public static IpPacket MapDestinationTestNetworkToLoopback(IpPacket ipPacket)
    {
        if (!IsRemoteTestAddress(ipPacket.SourceAddress) && !IsRemoteTestAddress(ipPacket.DestinationAddress))
            return ipPacket;

        if (ipPacket.DestinationAddress.Equals(RemoteTestIpV4)) {
            ipPacket.DestinationAddress = LocalTestIpV4;
            ipPacket.UpdateAllChecksums();
        }
        else if (ipPacket.DestinationAddress.Equals(RemoteTestIpV6)) {
            ipPacket.DestinationAddress = LocalTestIpV6;
            ipPacket.UpdateAllChecksums();
        }

        return ipPacket;
    }

    public static IpPacket RecoverTestNetworkToLoopback(IpPacket ipPacket)
    {
        if (!IsLocalTestAddress(ipPacket.SourceAddress) && !IsLocalTestAddress(ipPacket.DestinationAddress))
            return ipPacket;

        var isUpdated = false;
        if (ipPacket.SourceAddress.Equals(LocalTestIpV4)) {
            ipPacket.SourceAddress = RemoteTestIpV4;
            isUpdated = true;
        }
        else if (ipPacket.SourceAddress.Equals(LocalTestIpV6)) {
            ipPacket.SourceAddress = RemoteTestIpV6;
            isUpdated = true;
        }

        if (ipPacket.DestinationAddress.Equals(LocalTestIpV4)) {
            ipPacket.DestinationAddress = RemoteTestIpV4;
            isUpdated = true;
        }
        else if (ipPacket.DestinationAddress.Equals(LocalTestIpV6)) {
            ipPacket.DestinationAddress = RemoteTestIpV6;
            isUpdated = true;
        }

        if (isUpdated)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    private static bool IsRemoteTestAddress(IPAddress ipAddress)
    {
        return ipAddress.Equals(RemoteTestIpV4) || ipAddress.Equals(RemoteTestIpV6);
    }

    private static bool IsLocalTestAddress(IPAddress ipAddress)
    {
        return ipAddress.Equals(LocalTestIpV4) || ipAddress.Equals(LocalTestIpV6);
    }


}

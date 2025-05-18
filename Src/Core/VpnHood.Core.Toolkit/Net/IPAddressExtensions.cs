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

    public static bool IsMulticast(this IPAddress ipAddress)
    {
        return
            (ipAddress.IsV4() && IpNetwork.MulticastNetworkV4.Contains(ipAddress)) ||
            (ipAddress.IsV6() && ipAddress.IsIPv6Multicast);
    }

    public static bool IsLoopback(this IPAddress ipAddress)
    {
        // IPv4 loopback address is 127.x.x.x
        if (ipAddress.IsV4()) {
            Span<byte> buffer = stackalloc byte[4];
            return
                ipAddress.TryWriteBytes(buffer, out _) &&
                buffer[0] == 127;
        }

        // IPv6 loopback address is ::1
        if (ipAddress.IsV6()) {
            return ipAddress.Equals(IPAddress.IPv6Loopback);
        }

        return false;
    }


    public static bool SpanEquals(this IPAddress ipAddress, ReadOnlySpan<byte> ipAddressSpan)
    {
        if (ipAddress.IsV4() && ipAddressSpan.Length != 4) return false;
        if (ipAddress.IsV6() && ipAddressSpan.Length != 16) return false;

        return ipAddress
            .GetAddressBytesFast(stackalloc byte[16])
            .SequenceEqual(ipAddressSpan);
    }

    public static Span<byte> GetAddressBytesFast(this IPAddress ipAddress, Span<byte> buffer)
    {
        if (!ipAddress.TryWriteBytes(buffer, out var bytesWritten))
            throw new ArgumentException($"buffer is not big enough to hold the IP address. BufferLength: {buffer.Length}, IPAddress: {ipAddress}.");

        return buffer[..bytesWritten];
    }
}
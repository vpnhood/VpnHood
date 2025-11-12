using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Net;

// ReSharper disable once InconsistentNaming
public static class IPAddressExtensions
{
    extension(IPAddress ipAddress)
    {
        public bool IsV4()
        {
            return ipAddress.AddressFamily == AddressFamily.InterNetwork;
        }

        public bool IsV6()
        {
            return ipAddress.AddressFamily == AddressFamily.InterNetworkV6;
        }
    }

    extension(IPEndPoint ipEndPoint)
    {
        public bool IsV4()
        {
            return ipEndPoint.AddressFamily == AddressFamily.InterNetwork;
        }

        public bool IsV6()
        {
            return ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6;
        }
    }

    extension(AddressFamily addressFamily)
    {
        public bool IsV4()
        {
            return addressFamily == AddressFamily.InterNetwork;
        }

        public bool IsV6()
        {
            return addressFamily == AddressFamily.InterNetworkV6;
        }
    }

    extension(IPAddress ipAddress)
    {
        public bool IsMulticast()
        {
            return
                (ipAddress.IsV4() && IpNetwork.MulticastNetworkV4.Contains(ipAddress)) ||
                (ipAddress.IsV6() && ipAddress.IsIPv6Multicast);
        }

        public bool IsBroadcast()
        {
            return ipAddress.Equals(IPAddress.Broadcast);
        }

        public bool IsLoopback()
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

        public bool SpanEquals(ReadOnlySpan<byte> ipAddressSpan)
        {
            if (ipAddress.IsV4() && ipAddressSpan.Length != 4) return false;
            if (ipAddress.IsV6() && ipAddressSpan.Length != 16) return false;

            return ipAddress
                .GetAddressBytesFast(stackalloc byte[16])
                .SequenceEqual(ipAddressSpan);
        }

        public Span<byte> GetAddressBytesFast(Span<byte> buffer)
        {
            if (!ipAddress.TryWriteBytes(buffer, out var bytesWritten))
                throw new ArgumentException(
                    $"buffer is not big enough to hold the IP address. BufferLength: {buffer.Length}, IPAddress: {ipAddress}.");

            return buffer[..bytesWritten];
        }
    }
}
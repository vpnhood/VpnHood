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

    public static bool SpanEquals(this IPAddress ipAddress, ReadOnlySpan<byte> ipAddressSpan)
    {
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
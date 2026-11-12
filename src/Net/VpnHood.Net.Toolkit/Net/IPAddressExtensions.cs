using System.Net;
using System.Net.Sockets;

namespace VpnHood.Net.Toolkit.Net;

// ReSharper disable once InconsistentNaming
/// <summary>
/// Provides extension members for addresses, endpoints and address families: their version and kind checks, an
/// address's bytes, and its conversion to an <see cref="IpAddressValue"/>.
/// </summary>
public static class IPAddressExtensions
{
    /// <summary>Provides the conversion of an address.</summary>
    /// <param name="ipAddress">The address.</param>
    extension(IPAddress ipAddress)
    {
        /// <summary>Converts the address to a value with the same version, bytes and scope id.</summary>
        /// <returns>The value.</returns>
        public IpAddressValue ToValue() => new(ipAddress);
    }

    /// <summary>Provides the version checks of an address.</summary>
    /// <param name="ipAddress">The address.</param>
    extension(IPAddress ipAddress)
    {
        /// <summary>Gets whether the address is an IPv4 address.</summary>
        /// <value><see langword="true"/> if the address is IPv4; otherwise, <see langword="false"/>.</value>
        public bool IsV4 => ipAddress.AddressFamily == AddressFamily.InterNetwork;

        /// <summary>Gets whether the address is an IPv6 address.</summary>
        /// <value><see langword="true"/> if the address is IPv6; otherwise, <see langword="false"/>.</value>
        public bool IsV6 => ipAddress.AddressFamily == AddressFamily.InterNetworkV6;
    }

    /// <summary>Provides the version checks of an endpoint.</summary>
    /// <param name="ipEndPoint">The endpoint.</param>
    extension(IPEndPoint ipEndPoint)
    {
        /// <summary>Gets whether the endpoint's address is an IPv4 address.</summary>
        /// <value><see langword="true"/> if the address is IPv4; otherwise, <see langword="false"/>.</value>
        public bool IsV4 => ipEndPoint.AddressFamily == AddressFamily.InterNetwork;

        /// <summary>Gets whether the endpoint's address is an IPv6 address.</summary>
        /// <value><see langword="true"/> if the address is IPv6; otherwise, <see langword="false"/>.</value>
        public bool IsV6 => ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6;
    }

    /// <summary>Provides the version checks of an endpoint value.</summary>
    /// <param name="ipEndPoint">The endpoint.</param>
    extension(IpEndPointValue ipEndPoint)
    {
        /// <summary>Gets whether the endpoint's address is an IPv4 address.</summary>
        /// <value><see langword="true"/> if the address is IPv4; otherwise, <see langword="false"/>.</value>
        public bool IsV4 => ipEndPoint.AddressValue.IsV4;

        /// <summary>Gets whether the endpoint's address is an IPv6 address.</summary>
        /// <value><see langword="true"/> if the address is IPv6; otherwise, <see langword="false"/>.</value>
        public bool IsV6 => ipEndPoint.AddressValue.IsV6;
    }

    /// <summary>Provides the version checks of an address family.</summary>
    /// <param name="addressFamily">The address family.</param>
    extension(AddressFamily addressFamily)
    {
        /// <summary>Gets whether the family is IPv4, <see cref="AddressFamily.InterNetwork"/>.</summary>
        /// <value><see langword="true"/> if the family is IPv4; otherwise, <see langword="false"/>.</value>
        public bool IsV4 => addressFamily == AddressFamily.InterNetwork;

        /// <summary>Gets whether the family is IPv6, <see cref="AddressFamily.InterNetworkV6"/>.</summary>
        /// <value><see langword="true"/> if the family is IPv6; otherwise, <see langword="false"/>.</value>
        public bool IsV6 => addressFamily == AddressFamily.InterNetworkV6;
    }

    /// <summary>Provides the kind checks and the bytes of an address.</summary>
    /// <param name="ipAddress">The address.</param>
    extension(IPAddress ipAddress)
    {
        /// <summary>
        /// Gets whether the address is a multicast address: 224.0.0.0/4 for IPv4, ff00::/8 for IPv6.
        /// </summary>
        /// <value><see langword="true"/> if it is; otherwise, <see langword="false"/>.</value>
        public bool IsMulticast =>
            (ipAddress.IsV4 && IpNetwork.MulticastNetworkV4.Contains(ipAddress)) ||
            (ipAddress.IsV6 && ipAddress.IsIPv6Multicast);

        /// <summary>Gets whether the address is the IPv4 limited broadcast address, 255.255.255.255.</summary>
        /// <value><see langword="true"/> if it is; otherwise, <see langword="false"/>.</value>
        /// <remarks>A subnet's directed broadcast address is not recognized.</remarks>
        public bool IsBroadcast => ipAddress.Equals(IPAddress.Broadcast);

        /// <summary>
        /// Determines whether the address is a loopback address: 127.0.0.0/8 for IPv4, ::1 for IPv6.
        /// </summary>
        /// <returns><see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
        // A method, unlike the checks beside it: IPAddress's own static IsLoopback(IPAddress) holds the name, so a
        // property of that name does not compile (CS0428).
        public bool IsLoopback()
        {
            // IPv4 loopback address is 127.x.x.x
            if (ipAddress.IsV4) {
                Span<byte> buffer = stackalloc byte[4];
                return
                    ipAddress.TryWriteBytes(buffer, out _) &&
                    buffer[0] == 127;
            }

            // IPv6 loopback address is ::1
            if (ipAddress.IsV6) {
                return ipAddress.Equals(IPAddress.IPv6Loopback);
            }

            return false;
        }

        /// <summary>Determines whether the address has the given bytes.</summary>
        /// <param name="ipAddressSpan">The bytes, as a header holds them: 4 for IPv4, 16 for IPv6.</param>
        /// <returns>
        /// <see langword="true"/> if their length and value are the address's; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>An IPv6 address's scope id is not compared.</remarks>
        public bool SpanEquals(ReadOnlySpan<byte> ipAddressSpan)
        {
            if (ipAddress.IsV4 && ipAddressSpan.Length != 4) return false;
            if (ipAddress.IsV6 && ipAddressSpan.Length != 16) return false;

            return ipAddress
                .GetAddressBytesFast(stackalloc byte[16])
                .SequenceEqual(ipAddressSpan);
        }

        /// <summary>
        /// Writes the address's bytes to a buffer, with no array as <see cref="IPAddress.GetAddressBytes"/> makes.
        /// </summary>
        /// <param name="buffer">The buffer: 4 bytes or more for IPv4, 16 or more for IPv6.</param>
        /// <returns>The part of <paramref name="buffer"/> that holds the bytes.</returns>
        /// <exception cref="ArgumentException"><paramref name="buffer"/> is too short for the address.</exception>
        public Span<byte> GetAddressBytesFast(Span<byte> buffer)
        {
            if (!ipAddress.TryWriteBytes(buffer, out var bytesWritten))
                throw new ArgumentException(
                    $"buffer is not big enough to hold the IP address. BufferLength: {buffer.Length}, IPAddress: {ipAddress}.");

            return buffer[..bytesWritten];
        }

        /// <summary>
        /// Gets whether the address is in a benchmarking network: 198.18.0.0/15 for IPv4, 2001:2::/32 for IPv6.
        /// </summary>
        /// <value><see langword="true"/> if it is; otherwise, <see langword="false"/>.</value>
        public bool IsTestNetwork {
            get {
                Span<byte> bytes = stackalloc byte[16];
                if (!ipAddress.TryWriteBytes(bytes, out int bytesWritten))
                    return false;

                // IPv4
                if (bytesWritten == 4)
                    return bytes[0] == 198 && (bytes[1] & 254) == 18;

                // IPv6
                if (bytesWritten == 16)
                    return bytes[0] == 0x20 && bytes[1] == 0x01 &&
                           bytes[2] == 0x00 && bytes[3] == 0x02;

                return false;
            }
        }
    }
}


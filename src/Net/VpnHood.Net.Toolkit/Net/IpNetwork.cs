using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Represents an IP network in CIDR notation, such as <c>10.0.0.0/8</c> or <c>2001:db8::/32</c>: a prefix, and the
/// number of its leading bits that every address of the network shares.
/// </summary>
/// <remarks>
/// <para>
/// Its properties are read-only, and the addresses they return are its own, which a caller must not change. The prefix
/// is kept as given, host bits included, so <c>10.0.0.5/8</c> prints as such while its <see cref="FirstIpAddress"/> is
/// <c>10.0.0.0</c>; and two networks are equal when they hold the same addresses, so <c>10.0.0.5/8</c> equals
/// <c>10.0.0.0/8</c>.
/// </para>
/// <para>
/// A network of IPv4-mapped IPv6 addresses is the IPv4 network of those addresses, and is made so:
/// <c>::ffff:10.0.0.0/104</c> is <c>10.0.0.0/8</c>. A network that only reaches into the mapped addresses, as
/// <c>::/80</c> does, is an IPv6 network like any other; and an address asked about is asked as its IPv4 address
/// when it is mapped, which such a network does not hold.
/// </para>
/// <para>JSON reads and writes a network as its text, through <see cref="IpNetworkConverter"/>.</para>
/// </remarks>
[JsonConverter(typeof(IpNetworkConverter))]
public class IpNetwork
{
    // the length of ::ffff:0:0/96, which holds the IPv4-mapped addresses
    private const int MappedPrefixLength = 96;

    private readonly BigInteger _firstIpAddressValue;
    private readonly BigInteger _lastIpAddressValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpNetwork"/> class that holds one address: a /32 for IPv4, a /128
    /// for IPv6.
    /// </summary>
    /// <param name="prefix">The address.</param>
    public IpNetwork(IPAddress prefix)
        : this(prefix, prefix.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IpNetwork"/> class.</summary>
    /// <param name="prefix">
    /// An address of the network. Its host bits are left out of the network's addresses but kept in
    /// <see cref="Prefix"/>.
    /// </param>
    /// <param name="prefixLength">
    /// The number of leading bits the network's addresses share: 0 to 32 for IPv4, 0 to 128 for IPv6.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="prefixLength"/> is negative, above 32 for an IPv4 prefix, or above 128 for an IPv6 one.
    /// </exception>
    /// <remarks>
    /// An IPv4-mapped prefix with a length of 96 or more makes the IPv4 network of its IPv4 address, with a length
    /// 96 shorter.
    /// </remarks>
    public IpNetwork(IPAddress prefix, int prefixLength)
    {
        IPAddressUtil.Verify(prefix);

        // within the family's bits: the masks below shift by it, and by what is left of the bits
        ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixLength, prefix.AddressFamily.IsV6 ? 128 : 32);

        // Kept as IPv6, the network held no address a lookup asks about, as a mapped address is asked as IPv4
        if (prefix.IsIPv4MappedToIPv6 && prefixLength >= MappedPrefixLength) {
            prefix = prefix.MapToIPv4();
            prefixLength -= MappedPrefixLength;
        }

        var addressFamily = prefix.AddressFamily;
        var bits = addressFamily.IsV6 ? 128 : 32;
        Prefix = IPAddressUtil.WithoutScopeId(prefix);
        PrefixLength = prefixLength;
        var mask = ((BigInteger.One << prefixLength) - 1) << (bits - prefixLength);
        var maskNot = (BigInteger.One << (bits - prefixLength)) - 1;
        _firstIpAddressValue = IPAddressUtil.ToBigInteger(Prefix) & mask;
        _lastIpAddressValue = _firstIpAddressValue | maskNot;
        FirstIpAddress = IPAddressUtil.FromBigInteger(_firstIpAddressValue, addressFamily);
        LastIpAddress = IPAddressUtil.FromBigInteger(_lastIpAddressValue, addressFamily);
    }

    /// <summary>Gets the address the network was made with, host bits included.</summary>
    public IPAddress Prefix { get; }

    /// <summary>Gets the number of leading bits the network's addresses share.</summary>
    public int PrefixLength { get; }

    /// <summary>Gets the subnet mask of the prefix length, such as <c>255.255.255.0</c> for a /24.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="PrefixLength"/> is negative.</exception>
    public IPAddress SubnetMask => CidrToSubnetMask(PrefixLength, Prefix.AddressFamily);

    /// <summary>
    /// Gets the network's address family: <see cref="AddressFamily.InterNetwork"/> or
    /// <see cref="AddressFamily.InterNetworkV6"/>.
    /// </summary>
    public AddressFamily AddressFamily => Prefix.AddressFamily;

    /// <summary>Gets whether the network is an IPv4 network.</summary>
    /// <value><see langword="true"/> if it is; otherwise, <see langword="false"/>.</value>
    public bool IsV4 => Prefix.AddressFamily == AddressFamily.InterNetwork;

    /// <summary>Gets whether the network is an IPv6 network.</summary>
    /// <value><see langword="true"/> if it is; otherwise, <see langword="false"/>.</value>
    public bool IsV6 => Prefix.AddressFamily == AddressFamily.InterNetworkV6;

    /// <summary>Gets the network's first address: the prefix with its host bits cleared.</summary>
    public IPAddress FirstIpAddress { get; }

    /// <summary>Gets the network's last address: the prefix with its host bits set.</summary>
    public IPAddress LastIpAddress { get; }

    /// <summary>Gets the number of addresses in the network: 2 to the power of its host bits.</summary>
    public BigInteger Total => _lastIpAddressValue - _firstIpAddressValue + 1;

    /// <summary>Gets the network of every IPv4 address, <c>0.0.0.0/0</c>.</summary>
    public static IpNetwork AllV4 => field ??= new IpNetwork(IPAddress.Any, 0);

    /// <summary>
    /// Gets the IPv4 link-local network, <c>169.254.0.0/16</c>: the addresses an interface gives itself when no DHCP
    /// server answers (RFC 3927). They are never routed.
    /// </summary>
    public static IpNetwork LinkLocalNetworkV4 => field ??= Parse("169.254.0.0/16");

    /// <summary>
    /// Gets the IPv6 link-local network, <c>fe80::/10</c>, which every IPv6 interface has an address in, with no
    /// configuration. Its addresses are never routed.
    /// </summary>
    public static IpNetwork LinkLocalNetworkV6 => field ??= Parse("fe80::/10");

    /// <summary>
    /// Gets the IPv4 networks of local networks: the private <c>10.0.0.0/8</c>, <c>172.16.0.0/12</c> and
    /// <c>192.168.0.0/16</c> (RFC 1918), and the link-local <see cref="LinkLocalNetworkV4"/>.
    /// </summary>
    /// <remarks>
    /// Neither the loopback network <c>127.0.0.0/8</c> nor the carriers' shared space <c>100.64.0.0/10</c> is in it:
    /// <see cref="NonPublicNetworks"/> holds every network that is not public.
    /// </remarks>
    public static IReadOnlyList<IpNetwork> LocalNetworksV4 => field ??= [
        Parse("10.0.0.0/8"),
        Parse("172.16.0.0/12"),
        Parse("192.168.0.0/16"),
        LinkLocalNetworkV4
    ];

    /// <summary>Gets the IPv4 multicast network, <c>224.0.0.0/4</c>.</summary>
    public static IpNetwork MulticastNetworkV4 => field ??= new IpNetwork(IPAddress.Parse("224.0.0.0"), 4);

    /// <summary>Gets the IPv6 multicast network, <c>ff00::/8</c>.</summary>
    public static IpNetwork MulticastNetworkV6 => field ??= new IpNetwork(IPAddress.Parse("ff00::"), 8);

    /// <summary>Gets <see cref="MulticastNetworkV4"/> and <see cref="MulticastNetworkV6"/>.</summary>
    public static IReadOnlyList<IpNetwork> MulticastNetworks => field ??= [MulticastNetworkV4, MulticastNetworkV6];

    /// <summary>Gets the IPv4 loopback network, <c>127.0.0.0/8</c>.</summary>
    public static IpNetwork LoopbackNetworkV4 => field ??= Parse("127.0.0.0/8");

    /// <summary>Gets the IPv6 loopback address as a network, <c>::1/128</c>.</summary>
    public static IpNetwork LoopbackNetworkV6 => field ??= Parse("::1/128");

    /// <summary>Gets <see cref="LoopbackNetworkV4"/> and <see cref="LoopbackNetworkV6"/>.</summary>
    public static IReadOnlyList<IpNetwork> LoopbackNetworks => field ??= [LoopbackNetworkV4, LoopbackNetworkV6];

    /// <summary>Gets the network of every IPv6 address, <c>::/0</c>.</summary>
    public static IpNetwork AllV6 => field ??= new IpNetwork(IPAddress.IPv6Any, 0);

    /// <summary>
    /// Gets the IPv6 global unicast space, <c>2000::/3</c>, which holds every public IPv6 address assigned today.
    /// </summary>
    public static IpNetwork AllGlobalUnicastV6 => field ??= Parse("2000::/3");

    /// <summary>
    /// Gets the IPv6 networks outside the global unicast space <see cref="AllGlobalUnicastV6"/>: <c>::/3</c>,
    /// <c>4000::/2</c> and <c>8000::/1</c>.
    /// </summary>
    /// <remarks>
    /// So it holds the loopback, unique local, link-local and multicast addresses, and the space no registry assigns
    /// yet, where <see cref="LocalNetworksV4"/> holds the private and link-local networks alone.
    /// </remarks>
    public static IReadOnlyList<IpNetwork> LocalNetworksV6 => field ??= AllGlobalUnicastV6.Invert().ToArray();

    /// <summary>Gets <see cref="LocalNetworksV4"/> followed by <see cref="LocalNetworksV6"/>.</summary>
    public static IReadOnlyList<IpNetwork> LocalNetworks => field ??= LocalNetworksV4.Concat(LocalNetworksV6).ToArray();

    /// <summary>
    /// Gets the networks of both families whose addresses are not public: those the registries set aside and do not
    /// count as reachable from the internet, the multicast networks, and every IPv6 address outside the global unicast
    /// space <see cref="AllGlobalUnicastV6"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is the list for a guard that must keep a destination someone else names away from what only the machine or
    /// its network should reach: a server's own loopback, its local network, its provider's services. What a device
    /// takes as its local network is <see cref="LocalNetworks"/>.
    /// </para>
    /// <para>
    /// IPv4: <c>0.0.0.0/8</c>; the private <c>10.0.0.0/8</c>, <c>172.16.0.0/12</c> and <c>192.168.0.0/16</c>; the
    /// carriers' shared space <c>100.64.0.0/10</c>; the loopback <c>127.0.0.0/8</c>; the link-local
    /// <c>169.254.0.0/16</c>; the protocol assignments <c>192.0.0.0/24</c>; the documentation networks
    /// <c>192.0.2.0/24</c>, <c>198.51.100.0/24</c> and <c>203.0.113.0/24</c>; the benchmarking network
    /// <c>198.18.0.0/15</c>; the multicast <c>224.0.0.0/4</c>; and the reserved <c>240.0.0.0/4</c>, the broadcast
    /// address with it.
    /// </para>
    /// <para>
    /// IPv6: <c>::/3</c>, <c>4000::/2</c> and <c>8000::/1</c>, which hold the unspecified and loopback addresses, the
    /// NAT64 prefix <c>64:ff9b::/96</c>, the unique local, link-local and multicast networks and the space no
    /// registry assigns yet; and, inside the global unicast space, Teredo's <c>2001::/32</c>, the benchmarking network
    /// <c>2001:2::/48</c>, the old ORCHID network <c>2001:10::/28</c>, the documentation networks
    /// <c>2001:db8::/32</c> and <c>3fff::/20</c>, and 6to4's <c>2002::/16</c>.
    /// </para>
    /// <para>
    /// The rest of the protocol assignments <c>2001::/23</c> is not in it, as the registry counts its other parts
    /// as reachable. The two anycast addresses it counts so inside <c>192.0.0.0/24</c> are not left out; and where a
    /// network translates the NAT64 prefix, its addresses stand for public IPv4 hosts all the same.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<IpNetwork> NonPublicNetworks => field ??= [
        Parse("0.0.0.0/8"),
        Parse("10.0.0.0/8"),
        Parse("100.64.0.0/10"),
        LoopbackNetworkV4,
        LinkLocalNetworkV4,
        Parse("172.16.0.0/12"),
        Parse("192.0.0.0/24"),
        Parse("192.0.2.0/24"),
        Parse("192.168.0.0/16"),
        Parse("198.18.0.0/15"),
        Parse("198.51.100.0/24"),
        Parse("203.0.113.0/24"),
        MulticastNetworkV4,
        Parse("240.0.0.0/4"),
        Parse("::/3"),
        Parse("2001::/32"),
        Parse("2001:2::/48"),
        Parse("2001:10::/28"),
        Parse("2001:db8::/32"),
        Parse("2002::/16"),
        Parse("3fff::/20"),
        Parse("4000::/2"),
        Parse("8000::/1")
    ];

    /// <summary>Gets the networks of every address: <see cref="AllV4"/> and <see cref="AllV6"/>.</summary>
    public static IReadOnlyList<IpNetwork> All => field ??= [AllV4, AllV6];

    /// <summary>Gets an empty list of networks.</summary>
    public static IReadOnlyList<IpNetwork> None { get; } = [];

    /// <summary>Gets the fewest networks that hold exactly the addresses of a range.</summary>
    /// <param name="firstIpAddress">The range's first address.</param>
    /// <param name="lastIpAddress">The range's last address, of the same family.</param>
    /// <returns>
    /// The networks, from the lowest address up; none when <paramref name="lastIpAddress"/> is below
    /// <paramref name="firstIpAddress"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The two addresses are of different families.</exception>
    /// <remarks>
    /// The networks are made, and the families checked, as the result is enumerated. Two IPv4-mapped IPv6 addresses
    /// give the networks of their IPv4 addresses; a range that only reaches into the mapped addresses gives no network
    /// that lies wholly among them, as such a network holds no address a lookup asks about.
    /// </remarks>
    public static IEnumerable<IpNetwork> FromRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
    {
        if (firstIpAddress.AddressFamily != lastIpAddress.AddressFamily)
            throw new ArgumentException("AddressFamilies don't match!");

        if (firstIpAddress.IsIPv4MappedToIPv6 && lastIpAddress.IsIPv4MappedToIPv6) {
            firstIpAddress = firstIpAddress.MapToIPv4();
            lastIpAddress = lastIpAddress.MapToIPv4();
        }

        var addressFamily = firstIpAddress.AddressFamily;
        var bits = addressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        var first = IPAddressUtil.ToBigInteger(firstIpAddress);
        var last = IPAddressUtil.ToBigInteger(lastIpAddress);

        if (first > last) yield break;
        last++;
        // mask == 1 << len
        BigInteger mask = 1;
        var len = 0;
        while (first + mask <= last) {
            if ((first & mask) != 0) {
                if (!IsMappedNetwork(first, len, addressFamily))
                    yield return new IpNetwork(IPAddressUtil.FromBigInteger(first, addressFamily), bits - len);

                first += mask;
            }

            mask <<= 1;
            len++;
        }

        while (first < last) {
            mask >>= 1;
            len--;
            if ((last & mask) != 0) {
                if (!IsMappedNetwork(first, len, addressFamily))
                    yield return new IpNetwork(IPAddressUtil.FromBigInteger(first, addressFamily), bits - len);

                first += mask;
            }
        }
    }

    // A network of 2^hostBits addresses from a first address, wholly among the IPv4-mapped ones. The constructor would
    // make it the IPv4 network it is not: the whole mapped block, a piece of the range around it, would come out as
    // 0.0.0.0/0.
    private static bool IsMappedNetwork(BigInteger first, int hostBits, AddressFamily addressFamily)
    {
        return addressFamily == AddressFamily.InterNetworkV6 && hostBits <= 32 && first >> 32 == 0xFFFF;
    }

    private static IPAddress CidrToSubnetMask(int prefixLength, AddressFamily addressFamily)
    {
        switch (addressFamily) {
            case AddressFamily.InterNetwork:
                if (prefixLength is < 0 or > 32)
                    throw new ArgumentOutOfRangeException(nameof(prefixLength), "Invalid CIDR prefix length for IPv4.");

                // Guard prefixLength 0: in C# the shift count is masked to 5 bits, so
                // `uint.MaxValue << 32` is `<< 0` = 0xFFFFFFFF, which would wrongly yield the
                // 255.255.255.255 mask for a /0 (default route) instead of 0.0.0.0.
                var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
                var maskIpV4Bytes = BitConverter.GetBytes(mask);
                // IPAddress expects bytes in network (big-endian) order. On a little-endian host
                // BitConverter.GetBytes returns little-endian bytes, so reverse them in place.
                // NOTE: use Array.Reverse, NOT maskIpV4Bytes.Reverse() — the latter binds to the
                // LINQ/Enumerable overload that returns a discarded sequence (a no-op), which left
                // the mask byte-reversed (e.g. /24 -> 0.255.255.255 instead of 255.255.255.0).
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(maskIpV4Bytes);
                return new IPAddress(maskIpV4Bytes);

            case AddressFamily.InterNetworkV6:
                if (prefixLength is < 0 or > 128)
                    throw new ArgumentOutOfRangeException(nameof(prefixLength), "Invalid CIDR prefix length for IPv6.");

                var maskBytes = new byte[16];
                for (var i = 0; i < prefixLength / 8; i++) maskBytes[i] = 0xFF;
                if (prefixLength % 8 > 0) maskBytes[prefixLength / 8] = (byte)(0xFF << (8 - prefixLength % 8));
                return new IPAddress(maskBytes);

            default:
                throw new ArgumentException(
                    "Invalid Address Family. Use InterNetwork (IPv4) or InterNetworkV6 (IPv6).");
        }
    }

    /// <summary>Gets the networks of the addresses outside this network, within its own address family.</summary>
    /// <returns>The fewest networks that hold those addresses, from the lowest address up; none for a /0 network.</returns>
    public IOrderedEnumerable<IpNetwork> Invert()
    {
        return new[] { this }
            .ToIpRanges()
            .Invert(
                includeIPv4: AddressFamily == AddressFamily.InterNetwork,
                includeIPv6: AddressFamily == AddressFamily.InterNetworkV6)
            .ToIpNetworks();
    }

    /// <summary>Converts the network to the range from its first to its last address.</summary>
    /// <returns>The range.</returns>
    public IpRange ToIpRange()
    {
        return new IpRange(FirstIpAddress, LastIpAddress);
    }

    /// <summary>Determines whether an address is in the network.</summary>
    /// <param name="ipAddress">
    /// The address. An IPv4-mapped IPv6 address is asked about as its IPv4 address, and a scope id is ignored.
    /// </param>
    /// <returns><see langword="true"/> if the network holds the address; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipAddress"/> is <see langword="null"/>.</exception>
    public bool Contains(IPAddress ipAddress) => Contains(ipAddress.ToValue());

    /// <summary>Determines whether an address is in the network, with no object made.</summary>
    /// <param name="ipAddress">
    /// The address. An IPv4-mapped IPv6 address is asked about as its IPv4 address, and a scope id is ignored; the
    /// default value is in no network.
    /// </param>
    /// <returns><see langword="true"/> if the network holds the address; otherwise, <see langword="false"/>.</returns>
    public bool Contains(IpAddressValue ipAddress)
    {
        var value = ipAddress.IsIPv4MappedToIPv6 ? ipAddress.MapToIPv4() : ipAddress;
        return value.CompareAddress(FirstIpAddress.ToValue()) >= 0 &&
               value.CompareAddress(LastIpAddress.ToValue()) <= 0;
    }

    /// <summary>Converts the text form of a network, such as <c>10.0.0.0/8</c>, to an <see cref="IpNetwork"/>.</summary>
    /// <param name="value">An address, a slash and a prefix length.</param>
    /// <returns>The network.</returns>
    /// <exception cref="FormatException">
    /// <paramref name="value"/> is not an address, one slash and a prefix length of digits alone, or the length is
    /// above the family's bits.
    /// </exception>
    public static IpNetwork Parse(string value)
    {
        // an address, one slash and a prefix length of digits alone: nothing after it, no sign and no space
        var parts = value.Split('/');
        if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefixLength) &&
            prefixLength <= (prefix.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32) &&
            prefix.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            return new IpNetwork(prefix, prefixLength);

        throw new FormatException($"Could not parse IPNetwork from: {value}.");
    }

    /// <summary>
    /// Returns the network's text form: the prefix as given, a slash and the prefix length, such as
    /// <c>10.0.0.0/8</c>.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return $"{Prefix}/{PrefixLength}";
    }

    /// <inheritdoc/>
    /// <remarks>Two networks are equal when they hold the same addresses; the prefix's host bits do not count.</remarks>
    public override bool Equals(object? obj)
    {
        return obj is IpNetwork ipNetwork &&
               FirstIpAddress.Equals(ipNetwork.FirstIpAddress) &&
               LastIpAddress.Equals(ipNetwork.LastIpAddress);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(FirstIpAddress, LastIpAddress);
    }
}
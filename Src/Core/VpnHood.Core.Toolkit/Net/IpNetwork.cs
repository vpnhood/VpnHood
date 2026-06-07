using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace VpnHood.Core.Toolkit.Net;

// check IpNetwork2 for AOT
[JsonConverter(typeof(IpNetworkConverter))]
public class IpNetwork
{
    private BigInteger _firstIpAddressValue;
    private BigInteger _lastIpAddressValue;
    private bool _bigIntInitialized;
    private void EnsureBigInt()
    {
        if (_bigIntInitialized) return;
        // Defer BigInteger construction until first use; avoids triggering
        // System.Numerics paths during iOS NE static init.
        _firstIpAddressValue = new BigInteger(FirstIpAddress.GetAddressBytes(), isUnsigned: true, isBigEndian: true);
        _lastIpAddressValue = new BigInteger(LastIpAddress.GetAddressBytes(), isUnsigned: true, isBigEndian: true);
        _bigIntInitialized = true;
    }    public IpNetwork(IPAddress prefix)
        : this(prefix, prefix.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)
    {
    }

    public IpNetwork(IPAddress prefix, int prefixLength)
    {
        // Inline AddressFamily check to avoid triggering IPAddressUtil static cctor
        // (which has heavy eager static initializers that hang in iOS NE AOT sandbox).
        var af = prefix.AddressFamily;
        if (af != AddressFamily.InterNetwork && af != AddressFamily.InterNetworkV6)
            throw new NotSupportedException($"{af} is not supported!");

        Prefix = prefix;
        PrefixLength = prefixLength;

        var bits = af == AddressFamily.InterNetworkV6 ? 128 : 32;
        var mask = ((BigInteger.One << prefixLength) - 1) << (bits - prefixLength);
        var maskNot = (BigInteger.One << (bits - prefixLength)) - 1;

        var first = IPAddressUtil.ToBigInteger(Prefix) & mask;
        var last = first | maskNot;

        FirstIpAddress = IPAddressUtil.FromBigInteger(first, af);
        LastIpAddress = IPAddressUtil.FromBigInteger(last, af);
    }

    public IPAddress Prefix { get; }
    public int PrefixLength { get; }
    public IPAddress SubnetMask => CidrToSubnetMask(PrefixLength, Prefix.AddressFamily);
    public AddressFamily AddressFamily => Prefix.AddressFamily;
    public bool IsV4 => Prefix.AddressFamily == AddressFamily.InterNetwork;
    public bool IsV6 => Prefix.AddressFamily == AddressFamily.InterNetworkV6;
    public IPAddress FirstIpAddress { get; }
    public IPAddress LastIpAddress { get; }
    public BigInteger Total { get { EnsureBigInt(); return _lastIpAddressValue - _firstIpAddressValue + 1; } }

    public static IpNetwork AllV4 => field ??= new IpNetwork(IPAddress.Any, 0);

    public static IReadOnlyList<IpNetwork> LocalNetworksV4 => field ??= [
        Parse("10.0.0.0/8"),
        Parse("172.16.0.0/12"),
        Parse("192.168.0.0/16"),
        Parse("169.254.0.0/16")
    ];

    public static IpNetwork MulticastNetworkV4 => field ??= new(IPAddress.Parse("224.0.0.0"), 4);
    public static IpNetwork MulticastNetworkV6 => field ??= new(IPAddress.Parse("ff00::"), 8);
    public static IReadOnlyList<IpNetwork> MulticastNetworks => field ??= [MulticastNetworkV4, MulticastNetworkV6];
    public static IpNetwork LoopbackNetworkV4 => field ??= Parse("127.0.0.0/8");
    public static IpNetwork LoopbackNetworkV6 => field ??= Parse("::1/128");
    public static IReadOnlyList<IpNetwork> LoopbackNetworks => field ??= [LoopbackNetworkV4, LoopbackNetworkV6];
    public static IpNetwork AllV6 => field ??= new IpNetwork(IPAddress.IPv6Any, 0);
    public static IpNetwork AllGlobalUnicastV6 => field ??= Parse("2000::/3");

    // Lazy: AllGlobalUnicastV6.Invert() runs heavy LINQ/BigInteger chains that hang
    // inside iOS NetworkExtension AOT sandbox when triggered as part of static cctor.
    public static IReadOnlyList<IpNetwork> LocalNetworksV6 => field ??= AllGlobalUnicastV6.Invert().ToArray();

    public static IReadOnlyList<IpNetwork> LocalNetworks => field ??= LocalNetworksV4.Concat(LocalNetworksV6).ToArray();

    public static IReadOnlyList<IpNetwork> All => field ??= [AllV4, AllV6];
    public static IReadOnlyList<IpNetwork> None { get; } = [];

    public static IEnumerable<IpNetwork> FromRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
    {
        if (firstIpAddress.AddressFamily != lastIpAddress.AddressFamily)
            throw new ArgumentException("AddressFamilies don't match!");

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
                yield return new IpNetwork(IPAddressUtil.FromBigInteger(first, addressFamily), bits - len);
                first += mask;
            }
        }
    }

    private static IPAddress CidrToSubnetMask(int prefixLength, AddressFamily addressFamily)
    {
        switch (addressFamily) {
            case AddressFamily.InterNetwork:
                if (prefixLength is < 0 or > 32)
                    throw new ArgumentOutOfRangeException(nameof(prefixLength), "Invalid CIDR prefix length for IPv4.");

                var mask = uint.MaxValue << (32 - prefixLength);
                var maskIpV4Bytes = BitConverter.GetBytes(mask);
                // ReSharper disable once CSharp14OverloadResolutionWithSpanBreakingChange
                maskIpV4Bytes.Reverse();
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

    public IOrderedEnumerable<IpNetwork> Invert()
    {
        return new[] { this }
            .ToIpRanges()
            .Invert(
                includeIPv4: AddressFamily == AddressFamily.InterNetwork,
                includeIPv6: AddressFamily == AddressFamily.InterNetworkV6)
            .ToIpNetworks();
    }

    public IpRange ToIpRange()
    {
        return new IpRange(FirstIpAddress, LastIpAddress);
    }

    public bool Contains(IPAddress ipAddress)
    {
        return IPAddressUtil.Compare(ipAddress, FirstIpAddress) >= 0 &&
               IPAddressUtil.Compare(ipAddress, LastIpAddress) <= 0;
    }

    public static IpNetwork Parse(string value)
    {
        try {
            var parts = value.Split('/');
            return new IpNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
        }
        catch {
            throw new FormatException($"Could not parse IPNetwork from: {value}.");
        }
    }

    public override string ToString()
    {
        return $"{Prefix}/{PrefixLength}";
    }

    public override bool Equals(object? obj)
    {
        return obj is IpNetwork ipNetwork &&
               FirstIpAddress.Equals(ipNetwork.FirstIpAddress) &&
               LastIpAddress.Equals(ipNetwork.LastIpAddress);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FirstIpAddress, LastIpAddress);
    }
}
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace VpnHood.Core.Toolkit.Net;

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

        // Compute first/last IP using byte arithmetic instead of BigInteger.
        // BigInteger ops (left shifts, byte ctors) hang inside the iOS NetworkExtension
        // AOT sandbox, so avoid them on the hot init path.
        var prefixBytes = prefix.GetAddressBytes();
        var byteCount = prefixBytes.Length;
        var firstBytes = new byte[byteCount];
        var lastBytes = new byte[byteCount];
        var fullBytes = prefixLength / 8;
        var remBits = prefixLength % 8;
        for (var i = 0; i < fullBytes && i < byteCount; i++) {
            firstBytes[i] = prefixBytes[i];
            lastBytes[i] = prefixBytes[i];
        }
        if (fullBytes < byteCount) {
            var maskByte = remBits == 0 ? (byte)0 : (byte)(0xFF << (8 - remBits));
            firstBytes[fullBytes] = (byte)(prefixBytes[fullBytes] & maskByte);
            lastBytes[fullBytes] = (byte)(firstBytes[fullBytes] | (byte)~maskByte);
            for (var i = fullBytes + 1; i < byteCount; i++) {
                firstBytes[i] = 0;
                lastBytes[i] = 0xFF;
            }
        }

        FirstIpAddress = new IPAddress(firstBytes);
        LastIpAddress = new IPAddress(lastBytes);
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

    public static IpNetwork AllV4 => _allV4 ??= new IpNetwork(IPAddress.Any, 0);
    private static IpNetwork? _allV4;

    public static IpNetwork[] LocalNetworksV4 => _localNetworksV4 ??= [
        Parse("10.0.0.0/8"),
        Parse("172.16.0.0/12"),
        Parse("192.168.0.0/16"),
        Parse("169.254.0.0/16")
    ];
    private static IpNetwork[]? _localNetworksV4;

    public static IpNetwork MulticastNetworkV4 => _multicastNetworkV4 ??= new(IPAddress.Parse("224.0.0.0"), 4);
    private static IpNetwork? _multicastNetworkV4;
    public static IpNetwork MulticastNetworkV6 => _multicastNetworkV6 ??= new(IPAddress.Parse("ff00::"), 8);
    private static IpNetwork? _multicastNetworkV6;
    public static IpNetwork[] MulticastNetworks => _multicastNetworks ??= [MulticastNetworkV4, MulticastNetworkV6];
    private static IpNetwork[]? _multicastNetworks;
    public static IpNetwork LoopbackNetworkV4 => _loopbackNetworkV4 ??= Parse("127.0.0.0/8");
    private static IpNetwork? _loopbackNetworkV4;
    public static IpNetwork LoopbackNetworkV6 => _loopbackNetworkV6 ??= Parse("::1/128");
    private static IpNetwork? _loopbackNetworkV6;
    public static IpNetwork[] LoopbackNetworks => _loopbackNetworks ??= [LoopbackNetworkV4, LoopbackNetworkV6];
    private static IpNetwork[]? _loopbackNetworks;
    public static IpNetwork AllV6 => _allV6 ??= new IpNetwork(IPAddress.IPv6Any, 0);
    private static IpNetwork? _allV6;
    public static IpNetwork AllGlobalUnicastV6 => _allGlobalUnicastV6 ??= Parse("2000::/3");
    private static IpNetwork? _allGlobalUnicastV6;

    // Lazy: AllGlobalUnicastV6.Invert() runs heavy LINQ/BigInteger chains that hang
    // inside iOS NetworkExtension AOT sandbox when triggered as part of static cctor.
    private static IpNetwork[]? _localNetworksV6;
    public static IpNetwork[] LocalNetworksV6 => _localNetworksV6 ??= AllGlobalUnicastV6.Invert().ToArray();

    private static IpNetwork[]? _localNetworks;
    public static IpNetwork[] LocalNetworks => _localNetworks ??= LocalNetworksV4.Concat(LocalNetworksV6).ToArray();

    public static IpNetwork[] All => _all ??= [AllV4, AllV6];
    private static IpNetwork[]? _all;
    public static IpNetwork[] None { get; } = [];

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
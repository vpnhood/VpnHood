using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace VpnHood.Core.Toolkit.Net;

[JsonConverter(typeof(IpNetworkConverter))]
public class IpNetwork
{
    private readonly BigInteger _firstIpAddressValue;
    private readonly BigInteger _lastIpAddressValue;

    public IpNetwork(IPAddress prefix)
        : this(prefix, prefix.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)
    {
    }

    public IpNetwork(IPAddress prefix, int prefixLength)
    {
        IPAddressUtil.Verify(prefix);

        Prefix = prefix;
        PrefixLength = prefixLength;
        var bits = prefix.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        var mask = ((new BigInteger(1) << prefixLength) - 1) << (bits - prefixLength);
        var maskNot = (new BigInteger(1) << bits - prefixLength) - 1;
        _firstIpAddressValue = IPAddressUtil.ToBigInteger(Prefix) & mask;
        _lastIpAddressValue = _firstIpAddressValue | maskNot;
        FirstIpAddress = IPAddressUtil.FromBigInteger(_firstIpAddressValue, prefix.AddressFamily);
        LastIpAddress = IPAddressUtil.FromBigInteger(_lastIpAddressValue, prefix.AddressFamily);
    }

    public IPAddress Prefix { get; }
    public int PrefixLength { get; }
    public IPAddress SubnetMask => CidrToSubnetMask(PrefixLength, Prefix.AddressFamily);
    public AddressFamily AddressFamily => Prefix.AddressFamily;
    public bool IsV4 => Prefix.AddressFamily == AddressFamily.InterNetwork;
    public bool IsV6 => Prefix.AddressFamily == AddressFamily.InterNetworkV6;
    public IPAddress FirstIpAddress { get; }
    public IPAddress LastIpAddress { get; }
    public BigInteger Total => _lastIpAddressValue - _firstIpAddressValue + 1;

    public static IpNetwork AllV4 { get; } = Parse("0.0.0.0/0");

    public static IpNetwork[] LocalNetworksV4 { get; } = [
        Parse("10.0.0.0/8"),
        Parse("172.16.0.0/12"),
        Parse("192.168.0.0/16"),
        Parse("169.254.0.0/16")
    ];

    public static IpNetwork[] LoopbackNetworksV4 { get; } = [Parse("127.0.0.0/8")];
    public static IpNetwork[] LoopbackNetworksV6 { get; } = [Parse("::1/128")];
    public static IpNetwork[] LoopbackNetworks { get; } = LoopbackNetworksV4.Concat(LoopbackNetworksV6).ToArray();
    public static IpNetwork AllV6 { get; } = Parse("::/0");
    public static IpNetwork AllGlobalUnicastV6 { get; } = Parse("2000::/3");
    public static IpNetwork[] LocalNetworksV6 { get; } = AllGlobalUnicastV6.Invert().ToArray();
    public static IpNetwork[] LocalNetworks { get; } = LocalNetworksV4.Concat(LocalNetworksV6).ToArray();
    public static IpNetwork[] All { get; } = [AllV4, AllV6];
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
                return new IPAddress(BitConverter.GetBytes(mask).Reverse().ToArray());

            case AddressFamily.InterNetworkV6:
                if (prefixLength is < 0 or > 128)
                    throw new ArgumentOutOfRangeException(nameof(prefixLength), "Invalid CIDR prefix length for IPv6.");

                var maskBytes = new byte[16];
                for (var i = 0; i < prefixLength / 8; i++) maskBytes[i] = 0xFF;
                if (prefixLength % 8 > 0) maskBytes[prefixLength / 8] = (byte)(0xFF << (8 - (prefixLength % 8)));
                return new IPAddress(maskBytes);

            default:
                throw new ArgumentException("Invalid Address Family. Use InterNetwork (IPv4) or InterNetworkV6 (IPv6).");
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
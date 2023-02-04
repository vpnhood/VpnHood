using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace VpnHood.Common.Net;

[JsonConverter(typeof(IpNetworkConverter))]
public class IpNetwork
{
    private readonly BigInteger _firstIpAddressValue;
    private readonly BigInteger _lastIpAddressValue;

    public IpNetwork(IPAddress prefix)
        : this(prefix, prefix.AddressFamily==AddressFamily.InterNetwork ? 32 : 128)
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
    public AddressFamily AddressFamily => Prefix.AddressFamily;
    public bool IsIpV4 => Prefix.AddressFamily == AddressFamily.InterNetwork;
    public bool IsIpV6 => Prefix.AddressFamily == AddressFamily.InterNetworkV6;
    public IPAddress FirstIpAddress { get; }
    public IPAddress LastIpAddress { get; }
    public BigInteger Total => _lastIpAddressValue - _firstIpAddressValue + 1;

    public static IpNetwork AllV4 { get; } = Parse("0.0.0.0/0");
    public static IpNetwork[] LocalNetworksV4 { get; } =
    {
        Parse("10.0.0.0/8"),
        Parse("172.16.0.0/12"),
        Parse("192.168.0.0/16"),
        Parse("169.254.0.0/16")
    };

    public static IpNetwork[] LoopbackNetworksV4 { get; } = { Parse("127.0.0.0/8") };
    public static IpNetwork[] LoopbackNetworksV6 { get; } = { Parse("::1/128") };
    public static IpNetwork AllV6 { get; } = Parse("::/0");
    public static IpNetwork AllGlobalUnicastV6 { get; } = Parse("2000::/3");
    public static IpNetwork[] LocalNetworksV6 { get; } = AllGlobalUnicastV6.Invert();

    public static IEnumerable<IpNetwork> FromIpRange(IpRange ipRange)
    {
        return FromIpRange(ipRange.FirstIpAddress, ipRange.LastIpAddress);
    }

    public static IpNetwork[] FromIpRangeOld(IPAddress firstIpAddress, IPAddress lastIpAddress)
    {
        var firstIpAddressLong = IPAddressUtil.ToLong(firstIpAddress);
        var lastIpAddressLong = IPAddressUtil.ToLong(lastIpAddress);

        var result = new List<IpNetwork>();
        while (lastIpAddressLong >= firstIpAddressLong)
        {
            byte maxSize = 32;
            while (maxSize > 0)
            {
                var s = maxSize - 1;
                var mask = (long)(Math.Pow(2, 32) - Math.Pow(2, 32 - s));
                var maskBase = firstIpAddressLong & mask;

                if (maskBase != firstIpAddressLong)
                    break;

                maxSize--;
            }

            var x = Math.Log(lastIpAddressLong - firstIpAddressLong + 1) / Math.Log(2);
            var maxDiff = (byte)(32 - Math.Floor(x));
            if (maxSize < maxDiff) maxSize = maxDiff;
            var ipAddress = IPAddressUtil.FromLong(firstIpAddressLong);
            result.Add(new IpNetwork(ipAddress, maxSize));
            firstIpAddressLong += (long)Math.Pow(2, 32 - maxSize);
        }

        return result.ToArray();
    }

    public static IEnumerable<IpNetwork> FromIpRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
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
        int len = 0;
        while (first + mask <= last)
        {
            if ((first & mask) != 0)
            {
                yield return new IpNetwork(IPAddressUtil.FromBigInteger(first, addressFamily), bits - len);
                first += mask;
            }
            mask <<= 1;
            len++;
        }
        while (first < last)
        {
            mask >>= 1;
            len--;
            if ((last & mask) != 0)
            {
                yield return new IpNetwork(IPAddressUtil.FromBigInteger(first, addressFamily), bits - len);
                first += mask;
            }
        }
    }

    public IpNetwork[] Invert()
    {
        return Invert(new[] { this }, AddressFamily == AddressFamily.InterNetwork, AddressFamily == AddressFamily.InterNetworkV6);
    }

    public static IpNetwork Parse(string value)
    {
        try
        {
            var parts = value.Split('/');
            return new IpNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
        }
        catch
        {
            throw new FormatException($"Could not parse IPNetwork from: {value}!");
        }
    }

    public static IOrderedEnumerable<IpNetwork> Sort(IEnumerable<IpNetwork> ipNetworks)
    {
        return ipNetworks.OrderBy(x => x._firstIpAddressValue);
    }

    public static IpNetwork[] Invert(IEnumerable<IpNetwork> ipNetworks, bool includeIPv4 = true, bool includeIPv6 = true)
    {
        return FromIpRange(IpRange.Invert(ToIpRange(ipNetworks), includeIPv4, includeIPv6));
    }

    public IpRange ToIpRange()
    {
        return new IpRange(FirstIpAddress, LastIpAddress);
    }

    public static IpRange[] ToIpRange(IEnumerable<IpNetwork> ipNetworks)
    {
        return IpRange.Sort(ipNetworks.Select(x => x.ToIpRange()));
    }

    public static IpNetwork[] FromIpRange(IEnumerable<IpRange> ipRanges)
    {
        List<IpNetwork> ipNetworks = new();
        foreach (var ipRange in IpRange.Sort(ipRanges))
            ipNetworks.AddRange(FromIpRange(ipRange));
        return ipNetworks.ToArray();
    }

    public override string ToString()
    {
        return $"{Prefix}/{PrefixLength}";
    }

    public override bool Equals(object obj)
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
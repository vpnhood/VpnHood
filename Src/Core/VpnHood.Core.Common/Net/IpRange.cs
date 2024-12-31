using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Net;

// ReSharper disable PossibleMultipleEnumeration
[JsonConverter(typeof(IpRangeConverter))]
public class IpRange
{
    public IpRange(IPAddress ipAddress)
        : this(ipAddress, ipAddress)
    {
    }

    public IpRange(long firstIpAddress, long lastIpAddress)
        : this(IPAddressUtil.FromLong(firstIpAddress), IPAddressUtil.FromLong(lastIpAddress))
    {
    }

    public IpRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
    {
        if (firstIpAddress.AddressFamily != lastIpAddress.AddressFamily)
            throw new InvalidOperationException("Both ipAddress must have a same address family!");

        if (IPAddressUtil.Compare(firstIpAddress, lastIpAddress) > 0)
            throw new InvalidOperationException(
                $"{nameof(lastIpAddress)} must be equal or greater than {nameof(firstIpAddress)}");

        FirstIpAddress = firstIpAddress;
        LastIpAddress = lastIpAddress;
    }

    public static IpRange FromIpAddress(IPAddress ipAddress) => new(ipAddress);
    public bool IsIPv4MappedToIPv6 => FirstIpAddress.IsIPv4MappedToIPv6;
    public IpRange MapToIPv4() => new(FirstIpAddress.MapToIPv4(), LastIpAddress.MapToIPv4());
    public IpRange MapToIPv6() => new(FirstIpAddress.MapToIPv6(), LastIpAddress.MapToIPv6());
    public AddressFamily AddressFamily => FirstIpAddress.AddressFamily;
    public IPAddress FirstIpAddress { get; }
    public IPAddress LastIpAddress { get; }

    public BigInteger Total => new BigInteger(LastIpAddress.GetAddressBytes(), true, true) -
        new BigInteger(FirstIpAddress.GetAddressBytes(), true, true) + 1;

    public IEnumerable<IpNetwork> ToIpNetworks() => IpNetwork.FromRange(FirstIpAddress, LastIpAddress);

    public static IpRange Parse(string value)
    {
        if (value.IndexOf('/') != -1)
            return IpNetwork.Parse(value).ToIpRange();

        var items = value.Replace("to", "-").Split('-');
        return items.Length switch {
            1 => new IpRange(IPAddress.Parse(items[0].Trim())),
            2 => new IpRange(IPAddress.Parse(items[0].Trim()), IPAddress.Parse(items[1].Trim())),
            _ => throw new FormatException($"Could not parse the IpRange from: {value}")
        };
    }

    public override string ToString()
    {
        return FirstIpAddress.Equals(LastIpAddress) 
            ? $"{FirstIpAddress}" 
            : $"{FirstIpAddress}-{LastIpAddress}";
    }

    public override bool Equals(object? obj)
    {
        return obj is IpRange ipRange &&
               FirstIpAddress.Equals(ipRange.FirstIpAddress) &&
               LastIpAddress.Equals(ipRange.LastIpAddress);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FirstIpAddress, LastIpAddress);
    }

    public bool IsInRange(IPAddress ipAddress)
    {
        return
            IPAddressUtil.Compare(ipAddress, FirstIpAddress) >= 0 &&
            IPAddressUtil.Compare(ipAddress, LastIpAddress) <= 0;
    }
}
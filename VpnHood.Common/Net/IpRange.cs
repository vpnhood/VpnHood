using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace VpnHood.Common.Net;
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
            throw new InvalidOperationException($"{nameof(lastIpAddress)} must be equal or greater than {nameof(firstIpAddress)}");

        FirstIpAddress = firstIpAddress;
        LastIpAddress = lastIpAddress;
    }

    public AddressFamily AddressFamily => FirstIpAddress.AddressFamily;
    public IPAddress FirstIpAddress { get; }
    public IPAddress LastIpAddress { get; }
    public BigInteger Total => new BigInteger(LastIpAddress.GetAddressBytes(), true, true) - new BigInteger(FirstIpAddress.GetAddressBytes(), true, true) + 1;

    public static IOrderedEnumerable<IpRange> Sort(IEnumerable<IpRange> ipRanges, bool unify = true)
    {
        var sortedRanges = ipRanges.OrderBy(x => x.FirstIpAddress, new IPAddressComparer());
        return unify ? Unify(sortedRanges) : sortedRanges;
    }

    private static IOrderedEnumerable<IpRange> Unify(IEnumerable<IpRange> sortedIpRanges)
    {
        List<IpRange> res = new();
        foreach (var ipRange in sortedIpRanges)
        {
            if (res.Count > 0 &&
                ipRange.AddressFamily == res[^1].AddressFamily &&
                IPAddressUtil.Compare(IPAddressUtil.Decrement(ipRange.FirstIpAddress), res[^1].LastIpAddress) <= 0)
            {
                if (IPAddressUtil.Compare(ipRange.LastIpAddress, res[^1].LastIpAddress) > 0)
                    res[^1] = new IpRange(res[^1].FirstIpAddress, ipRange.LastIpAddress);
            }
            else
            {
                res.Add(ipRange);
            }
        }

        return res.OrderBy(x => x.FirstIpAddress, new IPAddressComparer());
    }

    public static IEnumerable<IpRange> Invert(IEnumerable<IpRange> ipRanges, bool includeIPv4 = true, bool includeIPv6 = true)
    {
        List<IpRange> list = new();

        // IP4
        if (includeIPv4)
        {
            var ipRanges2 = ipRanges.Where(x => x.AddressFamily == AddressFamily.InterNetwork);
            if (ipRanges2.Any())
                list.AddRange(InvertInternal(ipRanges2));
            else
                list.Add(new IpRange(IPAddressUtil.MinIPv4Value, IPAddressUtil.MaxIPv4Value));
        }

        // IP6
        if (includeIPv6)
        {
            var ipRanges2 = ipRanges.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6);
            if (ipRanges2.Any())
                list.AddRange(InvertInternal(ipRanges2));
            else
                list.Add(new IpRange(IPAddressUtil.MinIPv6Value, IPAddressUtil.MaxIPv6Value));
        }

        return list;
    }

    private static IEnumerable<IpRange> InvertInternal(IEnumerable<IpRange> ipRanges)
    {
        // sort
        var ipRangesSorted = Sort(ipRanges).ToArray();

        // extract
        List<IpRange> res = new();
        for (var i = 0; i < ipRangesSorted.Length; i++)
        {
            var ipRange = ipRangesSorted[i];
            var minIpValue = ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddressUtil.MinIPv6Value : IPAddressUtil.MinIPv4Value;
            var maxIpValue = ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddressUtil.MaxIPv6Value : IPAddressUtil.MaxIPv4Value;

            if (i == 0 && !IPAddressUtil.IsMinValue(ipRange.FirstIpAddress))
                res.Add(new IpRange(minIpValue, IPAddressUtil.Decrement(ipRange.FirstIpAddress)));

            if (i > 0)
                res.Add(new IpRange(IPAddressUtil.Increment(ipRangesSorted[i - 1].LastIpAddress), IPAddressUtil.Decrement(ipRange.FirstIpAddress)));

            if (i == ipRangesSorted.Length - 1 && !IPAddressUtil.IsMaxValue(ipRange.LastIpAddress))
                res.Add(new IpRange(IPAddressUtil.Increment(ipRange.LastIpAddress), maxIpValue));
        }

        return res;
    }

    public static IpRange Parse(string value)
    {
        var items = value.Replace("to", "-").Split('-');
        if (items.Length == 1)
            return new IpRange(IPAddress.Parse(items[0].Trim()));
        if (items.Length == 2)
            return new IpRange(IPAddress.Parse(items[0].Trim()), IPAddress.Parse(items[1].Trim()));
        throw new FormatException($"Could not parse {nameof(IpRange)} from: {value}!");
    }

    public override string ToString()
    {
        return $"{FirstIpAddress}-{LastIpAddress}";
    }

    public override bool Equals(object obj)
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

    /// <summary>
    ///     Search in ipRanges using binary search
    /// </summary>
    /// <param name="sortedIpRanges">a sorted ipRanges</param>
    /// <param name="ipAddress">search value</param>
    /// <returns></returns>
    public static bool IsInRangeFast(IpRange[] sortedIpRanges, IPAddress ipAddress)
    {
        return FindRangeFast(sortedIpRanges, ipAddress) != null;
    }

    /// <param name="sortedIpRanges">a sorted ipRanges</param>
    /// <param name="ipAddress">search value</param>
    public static IpRange? FindRangeFast(IpRange[] sortedIpRanges, IPAddress ipAddress)
    {
        var res = Array.BinarySearch(sortedIpRanges, new IpRange(ipAddress, ipAddress), new IpRangeSearchComparer());
        return res >= 0 && res < sortedIpRanges.Length ? sortedIpRanges[res] : null;
    }

    public static IEnumerable<IpRange> Exclude(IEnumerable<IpRange> ipRanges, IEnumerable<IpRange> excludeIpRanges)
    {
        return Invert(excludeIpRanges).Intersect(ipRanges);
    }

    public static IEnumerable<IpRange> Intersect(IEnumerable<IpRange> ipRanges1, IEnumerable<IpRange> ipRanges2)
    {
        // ReSharper disable once PossibleMultipleEnumeration
        var v4SortedRanges1 = ipRanges1
            .Where(x => x.AddressFamily == AddressFamily.InterNetwork)
            .OrderBy(x => x.FirstIpAddress, new IPAddressComparer());

        // ReSharper disable once PossibleMultipleEnumeration
        var v4SortedRanges2 = ipRanges2
            .Where(x => x.AddressFamily == AddressFamily.InterNetwork)
            .OrderBy(x => x.FirstIpAddress, new IPAddressComparer());

        // ReSharper disable once PossibleMultipleEnumeration
        var v6SortedRanges1 = ipRanges1
            .Where(x => x.AddressFamily == AddressFamily.InterNetworkV6)
            .OrderBy(x => x.FirstIpAddress, new IPAddressComparer());

        // ReSharper disable once PossibleMultipleEnumeration
        var v6SortedRanges2 = ipRanges2
            .Where(x => x.AddressFamily == AddressFamily.InterNetworkV6)
            .OrderBy(x => x.FirstIpAddress, new IPAddressComparer());


        var ipRangesV4 = IntersectInternal(v4SortedRanges1, v4SortedRanges2);
        var ipRangesV6 = IntersectInternal(v6SortedRanges1, v6SortedRanges2);
        var ret = ipRangesV4.Concat(ipRangesV6);
        return ret;
    }

    private static IEnumerable<IpRange> IntersectInternal(IEnumerable<IpRange> ipRanges1,
        IEnumerable<IpRange> ipRanges2)
    {
        ipRanges1 = Sort(ipRanges1);
        ipRanges2 = Sort(ipRanges2);

        var ipRanges = new List<IpRange>();
        foreach (var ipRange1 in ipRanges1)
            foreach (var ipRange2 in ipRanges2)
            {
                if (ipRange1.IsInRange(ipRange2.FirstIpAddress))
                    ipRanges.Add(new IpRange(ipRange2.FirstIpAddress,
                        IPAddressUtil.Min(ipRange1.LastIpAddress, ipRange2.LastIpAddress)));

                else if (ipRange1.IsInRange(ipRange2.LastIpAddress))
                    ipRanges.Add(new IpRange(IPAddressUtil.Max(ipRange1.FirstIpAddress, ipRange2.FirstIpAddress),
                        ipRange2.LastIpAddress));

                else if (ipRange2.IsInRange(ipRange1.FirstIpAddress))
                    ipRanges.Add(new IpRange(ipRange1.FirstIpAddress,
                        IPAddressUtil.Min(ipRange1.LastIpAddress, ipRange2.LastIpAddress)));

                else if (ipRange2.IsInRange(ipRange1.LastIpAddress))
                    ipRanges.Add(new IpRange(IPAddressUtil.Max(ipRange1.FirstIpAddress, ipRange2.FirstIpAddress),
                        ipRange1.LastIpAddress));
            }

        return Sort(ipRanges);
    }

    private class IpRangeSearchComparer : IComparer<IpRange>
    {
        public int Compare(IpRange x, IpRange y)
        {
            if (IPAddressUtil.Compare(x.FirstIpAddress, y.FirstIpAddress) <= 0 && IPAddressUtil.Compare(x.LastIpAddress, y.LastIpAddress) >= 0) return 0;
            if (IPAddressUtil.Compare(x.FirstIpAddress, y.FirstIpAddress) < 0) return -1;
            return +1;
        }
    }
}
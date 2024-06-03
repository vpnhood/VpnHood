﻿using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VpnHood.Common.Utils;

namespace VpnHood.Common.Net;

public class IpRangeOrderedList :
    IOrderedEnumerable<IpRange>,
    IReadOnlyList<IpRange>
{
    private readonly List<IpRange> _orderedList;
    public IOrderedEnumerable<IpRange> CreateOrderedEnumerable<TKey>(Func<IpRange, TKey> keySelector, IComparer<TKey> comparer, bool descending) =>
        descending ? _orderedList.OrderByDescending(keySelector, comparer) : _orderedList.OrderBy(keySelector, comparer);

    public IEnumerator<IpRange> GetEnumerator() => _orderedList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int Count => _orderedList.Count;
    public static IpRangeOrderedList Empty { get; } = new([]);

    public IpRange this[int index] => _orderedList[index];

    public IpRangeOrderedList()
    {
        _orderedList = [];
    }

    public IpRangeOrderedList(IEnumerable<IpRange> ipRanges)
    {
        _orderedList = Sort(ipRanges);
    }

    private IpRangeOrderedList(List<IpRange> orderedList)
    {
        _orderedList = orderedList;
    }


    public Task Save(string filePath)
    {
        return File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(_orderedList));
    }

    public bool IsAll()
    {
        return ToIpNetworks().IsAll();
    }

    public bool IsInRange(IPAddress ipAddress)
    {
        var res = _orderedList.BinarySearch(new IpRange(ipAddress, ipAddress), new IpRangeSearchComparer());
        return res >= 0;
    }

    public bool IsEmpty()
    {
        return ToIpNetworks().Any();
    }

    public IOrderedEnumerable<IpNetwork> ToIpNetworks()
    {
        var ipNetworkList = new List<IpNetwork>();
        foreach (var ipRange in _orderedList)
            ipNetworkList.AddRange(ipRange.ToIpNetworks());

        return ipNetworkList.OrderBy(x => x.FirstIpAddress, new IPAddressComparer());
    }

    public IpRangeOrderedList Union(IEnumerable<IpRange> ipRanges)
    {
        return new IpRangeOrderedList(_orderedList.Concat(ipRanges));
    }

    public IpRangeOrderedList Exclude(IPAddress ipAddress)
    {
        return Exclude(new IpRange(ipAddress));
    }

    public IpRangeOrderedList Exclude(IpRange ipRange)
    {
        return Exclude(new[] { ipRange });
    }

    public IpRangeOrderedList Exclude(IEnumerable<IpRange> ipRanges)
    {
        return Exclude(ipRanges.ToOrderedList());
    }

    public IpRangeOrderedList Exclude(IpRangeOrderedList ipRanges)
    {
        return Intersect(Invert(ipRanges));
    }

    public IpRangeOrderedList Intersect(IEnumerable<IpRange> ipRanges)
    {
        return Intersect(_orderedList, ipRanges.ToOrderedList());
    }

    public IpRangeOrderedList Intersect(IpRangeOrderedList ipRanges)
    {
        return Intersect(_orderedList, ipRanges);
    }

    public IpRangeOrderedList Invert(bool includeIPv4 = true, bool includeIPv6 = true)
    {
        return Invert(this, includeIPv4: includeIPv4, includeIPv6: includeIPv6);
    }

    private static List<IpRange> Sort(IEnumerable<IpRange> ipRanges)
    {
        var sortedRanges = ipRanges.OrderBy(x => x.FirstIpAddress, new IPAddressComparer());
        return Unify(sortedRanges);
    }

    private static List<IpRange> Unify(IOrderedEnumerable<IpRange> sortedIpRanges)
    {
        List<IpRange> res = [];
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

        return res;
    }

    private static IpRangeOrderedList Intersect(IEnumerable<IpRange> ipRanges1, IEnumerable<IpRange> ipRanges2)
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
        return ret.ToOrderedList();
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

        return ipRanges;
    }

    private static IpRangeOrderedList Invert(IpRangeOrderedList ipRanges,
        bool includeIPv4 = true, bool includeIPv6 = true)
    {
        //it is ordered as the following process does not change the order
        var newIpRanges = new List<IpRange>();

        // IP4
        if (includeIPv4)
        {
            var ipRanges2 = ipRanges.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
            if (ipRanges2.Any())
                newIpRanges.AddRange(InvertInternal(ipRanges2));
            else
                newIpRanges.Add(new IpRange(IPAddressUtil.MinIPv4Value, IPAddressUtil.MaxIPv4Value));
        }

        // IP6
        if (includeIPv6)
        {
            var ipRanges2 = ipRanges.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
            if (ipRanges2.Any())
                newIpRanges.AddRange(InvertInternal(ipRanges2));
            else
                newIpRanges.Add(new IpRange(IPAddressUtil.MinIPv6Value, IPAddressUtil.MaxIPv6Value));
        }

        return new IpRangeOrderedList(newIpRanges);
    }

    private static IEnumerable<IpRange> InvertInternal(IpRange[] orderedIpRanges)
    {
        // extract
        List<IpRange> res = [];
        for (var i = 0; i < orderedIpRanges.Length; i++)
        {
            var ipRange = orderedIpRanges[i];
            var minIpValue = ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddressUtil.MinIPv6Value : IPAddressUtil.MinIPv4Value;
            var maxIpValue = ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddressUtil.MaxIPv6Value : IPAddressUtil.MaxIPv4Value;

            if (i == 0 && !IPAddressUtil.IsMinValue(ipRange.FirstIpAddress))
                res.Add(new IpRange(minIpValue, IPAddressUtil.Decrement(ipRange.FirstIpAddress)));

            if (i > 0)
                res.Add(new IpRange(IPAddressUtil.Increment(orderedIpRanges[i - 1].LastIpAddress), IPAddressUtil.Decrement(ipRange.FirstIpAddress)));

            if (i == orderedIpRanges.Length - 1 && !IPAddressUtil.IsMaxValue(ipRange.LastIpAddress))
                res.Add(new IpRange(IPAddressUtil.Increment(ipRange.LastIpAddress), maxIpValue));
        }

        return res;
    }

    public static async Task<IpRangeOrderedList> Load(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var list = VhUtil.JsonDeserialize<IpRange[]>(json);
        return new IpRangeOrderedList(list);
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
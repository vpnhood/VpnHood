using System.Collections;
using System.Net;

namespace VpnHood.Common.Net;

public class IpRangeOrderedList(IEnumerable<IpRange> orderedList) : IOrderedEnumerable<IpRange>, IReadOnlyList<IpRange>
{
    private readonly List<IpRange> _orderedList = Sort(orderedList);

    public bool Exists(IPAddress ipAddress)
    {
        var res = _orderedList.BinarySearch(new IpRange(ipAddress, ipAddress), new IpRangeSearchComparer());
        return res >= 0;
    }

    public IpRangeOrderedList IntersectNew(IEnumerable<IpRange> ipRanges)
    {
        return new IpRangeOrderedList(IpRange.Intersect(_orderedList, ipRanges));
    }

    public IpRangeOrderedList ExcludeNew(IpRange ipRange)
    {
        return new IpRangeOrderedList(IpRange.Exclude(_orderedList, new[] { ipRange }));
    }

    public IpRangeOrderedList ExcludeNew(IEnumerable<IpRange> ipRanges)
    {
        return new IpRangeOrderedList(IpRange.Exclude(_orderedList, ipRanges));
    }

    public IpRangeOrderedList InvertNew(bool includeIPv4 = true, bool includeIPv6 = true)
    {
        return new IpRangeOrderedList(IpRange.Invert(_orderedList, includeIPv4, includeIPv6));
    }

    public IpRangeOrderedList UnionNew(IEnumerable<IpRange> ipRanges)
    {
        return new IpRangeOrderedList(_orderedList.Concat(ipRanges));
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

    public IOrderedEnumerable<IpRange> CreateOrderedEnumerable<TKey>(Func<IpRange, TKey> keySelector, IComparer<TKey> comparer, bool descending) =>
        descending ? _orderedList.OrderByDescending(keySelector, comparer) : _orderedList.OrderBy(keySelector, comparer);

    public IEnumerator<IpRange> GetEnumerator() => _orderedList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int Count => _orderedList.Count;
    public IpRange this[int index] => _orderedList[index];

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
using System.Collections;
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Net.Toolkit.Net;

public class IpRangeOrderedList :
    IOrderedEnumerable<IpRange>,
    IReadOnlyList<IpRange>
{
    private readonly List<IpRange> _orderedList;

    // the ranges' ends as values, for a search that makes no object (see BuildSearchRanges)
    private readonly IpAddressValue[] _searchFirsts;
    private readonly IpAddressValue[] _searchLasts;

    public IOrderedEnumerable<IpRange> CreateOrderedEnumerable<TKey>(
        Func<IpRange, TKey> keySelector,
        IComparer<TKey>? comparer, bool descending) =>
        descending
            ? _orderedList.OrderByDescending(keySelector, comparer)
            : _orderedList.OrderBy(keySelector, comparer);

    public IEnumerator<IpRange> GetEnumerator() => _orderedList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int Count => _orderedList.Count;
    public static IpRangeOrderedList Empty => field ??= new IpRangeOrderedList();

    public IpRange this[int index] => _orderedList[index];

    public IpRangeOrderedList()
        : this(new List<IpRange>())
    {
    }

    public IpRangeOrderedList(IEnumerable<IpRange> ipRanges)
        : this(Sort(ipRanges))
    {
    }

    // orderedList must be sorted so this constructor is private
    private IpRangeOrderedList(List<IpRange> orderedList)
    {
        _orderedList = orderedList;
        BuildSearchRanges(orderedList, out _searchFirsts, out _searchLasts);
    }

    public void Serialize(Stream stream)
    {
        Span<byte> firstIpBuffer = stackalloc byte[16];
        Span<byte> lastIpBuffer = stackalloc byte[16];
        // serialize to binary
        using var writer = new BinaryWriter(stream);
        writer.Write(_orderedList.Count);
        foreach (var range in _orderedList) {
            var firstIpBytes = range.FirstIpAddress.GetAddressBytesFast(firstIpBuffer);
            var lastIpBytes = range.LastIpAddress.GetAddressBytesFast(lastIpBuffer);

            writer.Write((byte)firstIpBytes.Length);
            writer.Write(firstIpBytes);
            writer.Write((byte)lastIpBytes.Length);
            writer.Write(lastIpBytes);
        }
    }

    public static IpRangeOrderedList Deserialize(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var length = reader.ReadInt32();
        var ipRanges = new IpRange[length];
        for (var i = 0; i < length; i++) {
            var firstIpLength = reader.ReadByte();
            var firstIpBytes = reader.ReadBytes(firstIpLength);
            var lastIpLength = reader.ReadByte();
            var lastIpBytes = reader.ReadBytes(lastIpLength);
            ipRanges[i] = new IpRange(new IPAddress(firstIpBytes), new IPAddress(lastIpBytes));
        }

        return new IpRangeOrderedList(ipRanges);
    }

    // Streams the serialized ranges as raw address bytes (4 for IPv4, 16 for IPv6) in file order,
    // without allocating IpRange/IPAddress objects and without sorting. The serialized stream is already
    // sorted+unified, so consumers that only need to persist the ranges (e.g. bulk-insert into SQLite)
    // can skip the object graph entirely. Note: BinaryReader disposes the stream when enumeration ends.
    public static IEnumerable<(byte[] Start, byte[] End)> DeserializeRaw(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var length = reader.ReadInt32();
        for (var i = 0; i < length; i++) {
            var firstIpLength = reader.ReadByte();
            var firstIpBytes = reader.ReadBytes(firstIpLength);
            var lastIpLength = reader.ReadByte();
            var lastIpBytes = reader.ReadBytes(lastIpLength);
            yield return (firstIpBytes, lastIpBytes);
        }
    }

    public bool IsAll()
    {
        // use ToIpRanges for All to improve performance
        return IpNetwork.All
            .ToIpRanges()
            .SequenceEqual(this);
    }

    public bool IsAllV4()
    {
        // use ToIpRanges for All to improve performance
        return new[] { IpNetwork.AllV4 }
            .ToIpRanges()
            .SequenceEqual(this);
    }

    public bool IsAllV6()
    {
        // use ToIpRanges for All to improve performance
        return new[] { IpNetwork.AllV6 }
            .ToIpRanges()
            .SequenceEqual(this);
    }

    /// <summary>Determines whether an address is in one of the ranges.</summary>
    /// <param name="ipAddress">
    /// The address. An IPv4-mapped IPv6 address is looked up as its IPv4 address, and a scope id is ignored, as
    /// <see cref="IPAddressUtil.Compare(IPAddress, IPAddress)"/> orders addresses.
    /// </param>
    /// <returns><see langword="true"/> if a range holds the address; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipAddress"/> is <see langword="null"/>.</exception>
    public bool Contains(IPAddress ipAddress) => Contains(ipAddress.ToValue());

    /// <summary>Determines whether an address is in one of the ranges, with no object made.</summary>
    /// <param name="ipAddress">
    /// The address. An IPv4-mapped IPv6 address is looked up as its IPv4 address, and a scope id is ignored, as
    /// <see cref="IPAddressUtil.Compare(IpAddressValue, IpAddressValue)"/> orders addresses; the default value is in
    /// no range.
    /// </param>
    /// <returns><see langword="true"/> if a range holds the address; otherwise, <see langword="false"/>.</returns>
    public bool Contains(IpAddressValue ipAddress)
    {
        // the last range that starts at or before the address is the only one that may hold it
        var low = 0;
        var high = _searchFirsts.Length - 1;
        while (low <= high) {
            var middle = low + (high - low) / 2;
            if (IPAddressUtil.Compare(_searchFirsts[middle], ipAddress) <= 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return high >= 0 && IPAddressUtil.Compare(ipAddress, _searchLasts[high]) <= 0;
    }

    public bool IsNone()
    {
        return _orderedList.Count == 0;
    }

    public IOrderedEnumerable<IpNetwork> ToIpNetworks()
    {
        var ipNetworkList = new List<IpNetwork>();
        foreach (var ipRange in _orderedList)
            ipNetworkList.AddRange(ipRange.ToIpNetworks());

        return ipNetworkList.OrderBy(x => x.FirstIpAddress, new IPAddressComparer());
    }

    public IpRangeOrderedList Union(IPAddress ipAddress)
    {
        return Union(new IpRange(ipAddress));
    }

    public IpRangeOrderedList Union(IpRange ipRange)
    {
        return Union([ipRange]);
    }

    public IpRangeOrderedList Union(IEnumerable<IpRange> ipRanges)
    {
        // ReSharper disable PossibleMultipleEnumeration
        return ipRanges.Any() ? new IpRangeOrderedList(_orderedList.Concat(ipRanges)) : this;
        // ReSharper restore PossibleMultipleEnumeration
    }

    public IpRangeOrderedList Exclude(IPAddress ipAddress)
    {
        return Exclude(new IpRange(ipAddress));
    }

    public IpRangeOrderedList Exclude(IpRange ipRange)
    {
        return Exclude([ipRange]);
    }

    public IpRangeOrderedList Exclude(IEnumerable<IpRange> ipRanges)
    {
        return Exclude(ipRanges.ToOrderedList());
    }

    public IpRangeOrderedList Exclude(IpRangeOrderedList ipRanges)
    {
        return Intersect(ipRanges.Invert());
    }

    public IpRangeOrderedList Intersect(IEnumerable<IpRange> ipRanges)
    {
        return Intersect(ipRanges.ToOrderedList());
    }

    public IpRangeOrderedList Intersect(IpRangeOrderedList ipRanges)
    {
        return Intersect(this, ipRanges);
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

    private static List<IpRange> Unify(IEnumerable<IpRange> sortedIpRanges)
    {
        List<IpRange> res = [];
        foreach (var ipRange in sortedIpRanges) {
            if (res.Count > 0 &&
                ipRange.AddressFamily == res[^1].AddressFamily &&
                IPAddressUtil.Compare(IPAddressUtil.Decrement(ipRange.FirstIpAddress), res[^1].LastIpAddress) <= 0) {
                if (IPAddressUtil.Compare(ipRange.LastIpAddress, res[^1].LastIpAddress) > 0)
                    res[^1] = new IpRange(res[^1].FirstIpAddress, ipRange.LastIpAddress);
            }
            else {
                res.Add(ipRange);
            }
        }

        return res;
    }

    private static IpRangeOrderedList Intersect(IpRangeOrderedList ipRanges1, IpRangeOrderedList ipRanges2)
    {
        // performance optimization
        if (ipRanges1.IsAll()) return ipRanges2;
        if (ipRanges2.IsAll()) return ipRanges1;

        var v4SortedRanges1 = ipRanges1
            .Where(x => x.AddressFamily == AddressFamily.InterNetwork);

        var v4SortedRanges2 = ipRanges2
            .Where(x => x.AddressFamily == AddressFamily.InterNetwork);

        var v6SortedRanges1 = ipRanges1
            .Where(x => x.AddressFamily == AddressFamily.InterNetworkV6);

        var v6SortedRanges2 = ipRanges2
            .Where(x => x.AddressFamily == AddressFamily.InterNetworkV6);

        //all range are ordered as the following process does not change the order
        var ipRangesV4 = IntersectInternal(v4SortedRanges1, [.. v4SortedRanges2]);
        var ipRangesV6 = IntersectInternal(v6SortedRanges1, [.. v6SortedRanges2]);
        var ret = ipRangesV4.Concat(ipRangesV6);

        return new IpRangeOrderedList(ret);
    }

    private static IEnumerable<IpRange> IntersectInternal(
        IEnumerable<IpRange> orderedIpRanges1,
        IpRange[] orderedIpRanges2)
    {
        var ipRanges = new List<IpRange>();
        foreach (var ipRange1 in orderedIpRanges1)
        foreach (var ipRange2 in orderedIpRanges2) {
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
        //it is ordered so the following process does not change the order
        var newIpRanges = new List<IpRange>();

        // IP4
        if (includeIPv4) {
            var ipRanges2 = ipRanges.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
            if (ipRanges2.Any())
                newIpRanges.AddRange(InvertInternal(ipRanges2));
            else
                newIpRanges.Add(new IpRange(IPAddressUtil.MinIPv4Value, IPAddressUtil.MaxIPv4Value));
        }

        // IP6
        if (includeIPv6) {
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
        for (var i = 0; i < orderedIpRanges.Length; i++) {
            var ipRange = orderedIpRanges[i];
            var minIpValue = ipRange.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddressUtil.MinIPv6Value
                : IPAddressUtil.MinIPv4Value;
            var maxIpValue = ipRange.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddressUtil.MaxIPv6Value
                : IPAddressUtil.MaxIPv4Value;

            if (i == 0 && !IPAddressUtil.IsMinValue(ipRange.FirstIpAddress))
                res.Add(new IpRange(minIpValue, IPAddressUtil.Decrement(ipRange.FirstIpAddress)));

            if (i > 0)
                res.Add(new IpRange(IPAddressUtil.Increment(orderedIpRanges[i - 1].LastIpAddress),
                    IPAddressUtil.Decrement(ipRange.FirstIpAddress)));

            if (i == orderedIpRanges.Length - 1 && !IPAddressUtil.IsMaxValue(ipRange.LastIpAddress))
                res.Add(new IpRange(IPAddressUtil.Increment(ipRange.LastIpAddress), maxIpValue));
        }

        return res;
    }

    // The list is in IPAddressUtil.Compare's order, which the search uses too, but Unify merges ranges only within an
    // address family: an IPv4-mapped range may overlap an IPv4 one, as Compare takes a mapped address as IPv4. Ranges
    // that overlap are merged here, so the last range that starts at or before an address is the only one that may
    // hold it.
    private static void BuildSearchRanges(List<IpRange> orderedList,
        out IpAddressValue[] firsts, out IpAddressValue[] lasts)
    {
        var firstList = new List<IpAddressValue>(orderedList.Count);
        var lastList = new List<IpAddressValue>(orderedList.Count);
        foreach (var ipRange in orderedList) {
            var first = ipRange.FirstIpAddress.ToValue();
            var last = ipRange.LastIpAddress.ToValue();
            if (lastList.Count > 0 && IPAddressUtil.Compare(first, lastList[^1]) <= 0) {
                if (IPAddressUtil.Compare(last, lastList[^1]) > 0)
                    lastList[^1] = last;
            }
            else {
                firstList.Add(first);
                lastList.Add(last);
            }
        }

        firsts = firstList.ToArray();
        lasts = lastList.ToArray();
    }
}
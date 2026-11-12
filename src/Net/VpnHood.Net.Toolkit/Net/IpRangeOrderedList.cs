using System.Collections;
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Represents a sorted set of IP ranges of both families, in which the ranges of a family that overlap or touch are
/// merged into one.
/// </summary>
/// <remarks>
/// <para>
/// The list cannot be changed: <see cref="Union(IEnumerable{IpRange})"/>, <see cref="Exclude(IpRangeOrderedList)"/>,
/// <see cref="Intersect(IpRangeOrderedList)"/> and <see cref="Invert(bool, bool)"/> return another list, or this one
/// where nothing changes. The ranges are in <see cref="IPAddressUtil.Compare(IPAddress, IPAddress)"/>'s order of their
/// first addresses, and <see cref="Contains(IpAddressValue)"/> finds an address by binary search.
/// </para>
/// <para>
/// That order takes an IPv4-mapped IPv6 address as its IPv4 address, so a range of such addresses sorts among the
/// IPv4 ranges, while merging, inverting and intersecting take it with the IPv6 ranges. One between two IPv4 ranges
/// keeps them apart, unmerged; inverting or excluding next to one can make a range whose ends are out of order, which
/// throws <see cref="InvalidOperationException"/>; and an IPv4 range and a mapped one never intersect.
/// </para>
/// <para>
/// <see cref="IsAll"/>, <see cref="IsAllV6"/> and <see cref="Invert(bool, bool)"/> compare ends with
/// <see cref="IPAddress.Equals(object)"/>, scope id included: a scoped end can make the first two
/// <see langword="false"/>, and inverting <c>::%3</c> keeps <c>::</c>.
/// </para>
/// </remarks>
public class IpRangeOrderedList :
    IOrderedEnumerable<IpRange>,
    IReadOnlyList<IpRange>
{
    private readonly List<IpRange> _orderedList;

    // the ranges' ends as values, for a search that makes no object (see BuildSearchRanges)
    private readonly IpAddressValue[] _searchFirsts;
    private readonly IpAddressValue[] _searchLasts;

    /// <summary>Sorts the ranges by a key, for <c>ThenBy</c> and <c>ThenByDescending</c>.</summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="keySelector">The function that gets a range's key.</param>
    /// <param name="comparer">The comparer of keys, or <see langword="null"/> for the default one.</param>
    /// <param name="descending">
    /// <see langword="true"/> to sort by the key in descending order; <see langword="false"/> for ascending.
    /// </param>
    /// <returns>The ranges, sorted by the key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike other ordered sequences, the key comes first and this list's order breaks its ties only, so
    /// <c>list.ThenBy(key)</c> sorts the ranges as <c>list.OrderBy(key)</c> would.
    /// </remarks>
    public IOrderedEnumerable<IpRange> CreateOrderedEnumerable<TKey>(
        Func<IpRange, TKey> keySelector,
        IComparer<TKey>? comparer, bool descending) =>
        descending
            ? _orderedList.OrderByDescending(keySelector, comparer)
            : _orderedList.OrderBy(keySelector, comparer);

    /// <inheritdoc/>
    public IEnumerator<IpRange> GetEnumerator() => _orderedList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Gets the number of ranges, after merging.</summary>
    public int Count => _orderedList.Count;

    /// <summary>Gets an empty list.</summary>
    public static IpRangeOrderedList Empty => field ??= new IpRangeOrderedList();

    /// <summary>Gets the range at an index.</summary>
    /// <param name="index">The index, from 0 to <see cref="Count"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is negative, or not below <see cref="Count"/>.
    /// </exception>
    public IpRange this[int index] => _orderedList[index];

    /// <summary>Initializes a new instance of the <see cref="IpRangeOrderedList"/> class that holds no range.</summary>
    public IpRangeOrderedList()
        : this(new List<IpRange>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpRangeOrderedList"/> class that holds the given ranges, sorted,
    /// with those of a family that overlap or touch merged.
    /// </summary>
    /// <param name="ipRanges">The ranges, in any order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ipRanges"/> is <see langword="null"/>.</exception>
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

    /// <summary>Writes the ranges to a stream in a compact binary form, which <see cref="Deserialize"/> reads.</summary>
    /// <param name="stream">The stream. It is closed when the method returns.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot be written.</exception>
    /// <remarks>
    /// The form is the number of ranges, as a 32-bit little-endian integer, then for each range the length (4 or 16)
    /// and bytes of its first address, then those of its last. Scope ids are not written.
    /// </remarks>
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

    /// <summary>Reads ranges that <see cref="Serialize"/> wrote.</summary>
    /// <param name="stream">The stream, at the start of the data. It is closed when the method returns.</param>
    /// <returns>The list, sorted and merged again.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="EndOfStreamException">The stream ends before the number of ranges or an address's length.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="stream"/> cannot be read, or an address's bytes are neither 4 nor 16, as when the stream ends
    /// inside an address.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A range's addresses are of different families, or out of order.
    /// </exception>
    /// <exception cref="OverflowException">The number of ranges is negative.</exception>
    /// <remarks>
    /// An address's bytes are not checked against its length: a last address cut short to 4 of its 16 bytes reads as
    /// an IPv4 address.
    /// </remarks>
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

    /// <summary>
    /// Reads ranges that <see cref="Serialize"/> wrote as their addresses' bytes, with no <see cref="IpRange"/> or
    /// <see cref="IPAddress"/> made, and no sorting.
    /// </summary>
    /// <param name="stream">The stream, at the start of the data. It is closed when the enumeration ends.</param>
    /// <returns>
    /// Each range's first and last address bytes, 4 for IPv4 and 16 for IPv6, in the stream's order, which
    /// <see cref="Serialize"/> wrote sorted and merged. Nothing is read until the result is enumerated, and the lengths
    /// are not checked: a last address the stream's end cuts short comes back short.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> is <see langword="null"/>, when the enumeration starts.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot be read, when the enumeration starts.</exception>
    /// <exception cref="EndOfStreamException">The stream ends before the number of ranges or an address's length.</exception>
    /// <remarks>For a caller that only stores the ranges, such as a bulk insert into a database.</remarks>
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

    /// <summary>Determines whether the list holds every address, IPv4 and IPv6.</summary>
    /// <returns><see langword="true"/> if it does; otherwise, <see langword="false"/>.</returns>
    public bool IsAll()
    {
        // use ToIpRanges for All to improve performance
        return IpNetwork.All
            .ToIpRanges()
            .SequenceEqual(this);
    }

    /// <summary>Determines whether the list holds every IPv4 address and no IPv6 one.</summary>
    /// <returns><see langword="true"/> if it does; otherwise, <see langword="false"/>.</returns>
    public bool IsAllV4()
    {
        // use ToIpRanges for All to improve performance
        return new[] { IpNetwork.AllV4 }
            .ToIpRanges()
            .SequenceEqual(this);
    }

    /// <summary>Determines whether the list holds every IPv6 address and no IPv4 one.</summary>
    /// <returns><see langword="true"/> if it does; otherwise, <see langword="false"/>.</returns>
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

    /// <summary>Determines whether the list holds no range.</summary>
    /// <returns><see langword="true"/> if it holds none; otherwise, <see langword="false"/>.</returns>
    public bool IsNone()
    {
        return _orderedList.Count == 0;
    }

    /// <summary>Converts each range to the fewest networks that hold its addresses.</summary>
    /// <returns>
    /// The networks, in <see cref="IPAddressUtil.Compare(IPAddress, IPAddress)"/>'s order of their first addresses.
    /// </returns>
    public IOrderedEnumerable<IpNetwork> ToIpNetworks()
    {
        var ipNetworkList = new List<IpNetwork>();
        foreach (var ipRange in _orderedList)
            ipNetworkList.AddRange(ipRange.ToIpNetworks());

        return ipNetworkList.OrderBy(x => x.FirstIpAddress, new IPAddressComparer());
    }

    /// <summary>Returns a list of this list's addresses and one more.</summary>
    /// <param name="ipAddress">The address to add.</param>
    /// <returns>The new list.</returns>
    public IpRangeOrderedList Union(IPAddress ipAddress)
    {
        return Union(new IpRange(ipAddress));
    }

    /// <summary>Returns a list of this list's addresses and those of a range.</summary>
    /// <param name="ipRange">The range to add.</param>
    /// <returns>The new list.</returns>
    public IpRangeOrderedList Union(IpRange ipRange)
    {
        return Union([ipRange]);
    }

    /// <summary>Returns a list of this list's addresses and those of other ranges.</summary>
    /// <param name="ipRanges">The ranges to add, in any order.</param>
    /// <returns>The new list; this list itself when <paramref name="ipRanges"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipRanges"/> is <see langword="null"/>.</exception>
    public IpRangeOrderedList Union(IEnumerable<IpRange> ipRanges)
    {
        // ReSharper disable PossibleMultipleEnumeration
        return ipRanges.Any() ? new IpRangeOrderedList(_orderedList.Concat(ipRanges)) : this;
        // ReSharper restore PossibleMultipleEnumeration
    }

    /// <summary>Returns a list of this list's addresses but one.</summary>
    /// <param name="ipAddress">The address to leave out.</param>
    /// <returns>The new list.</returns>
    /// <exception cref="InvalidOperationException">
    /// A range's ends would compare out of order, which happens only with IPv4-mapped addresses: see the remarks of
    /// <see cref="IpRangeOrderedList"/>.
    /// </exception>
    public IpRangeOrderedList Exclude(IPAddress ipAddress)
    {
        return Exclude(new IpRange(ipAddress));
    }

    /// <summary>Returns a list of this list's addresses outside a range.</summary>
    /// <param name="ipRange">The range to leave out.</param>
    /// <returns>The new list.</returns>
    /// <exception cref="InvalidOperationException">
    /// A range's ends would compare out of order, which happens only with IPv4-mapped addresses: see the remarks of
    /// <see cref="IpRangeOrderedList"/>.
    /// </exception>
    public IpRangeOrderedList Exclude(IpRange ipRange)
    {
        return Exclude([ipRange]);
    }

    /// <summary>Returns a list of this list's addresses outside other ranges.</summary>
    /// <param name="ipRanges">The ranges to leave out, in any order.</param>
    /// <returns>The new list, which may be this list itself when <paramref name="ipRanges"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipRanges"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A range's ends would compare out of order, which happens only with IPv4-mapped addresses: see the remarks of
    /// <see cref="IpRangeOrderedList"/>.
    /// </exception>
    public IpRangeOrderedList Exclude(IEnumerable<IpRange> ipRanges)
    {
        return Exclude(ipRanges.ToOrderedList());
    }

    /// <summary>Returns a list of this list's addresses outside another list.</summary>
    /// <param name="ipRanges">The ranges to leave out.</param>
    /// <returns>The new list, which may be this list itself when <paramref name="ipRanges"/> is empty.</returns>
    /// <exception cref="InvalidOperationException">
    /// A range's ends would compare out of order, which happens only with IPv4-mapped addresses: see the remarks of
    /// <see cref="IpRangeOrderedList"/>.
    /// </exception>
    public IpRangeOrderedList Exclude(IpRangeOrderedList ipRanges)
    {
        return Intersect(ipRanges.Invert());
    }

    /// <summary>Returns a list of the addresses that this list and other ranges both hold.</summary>
    /// <param name="ipRanges">The other ranges, in any order.</param>
    /// <returns>
    /// The new list; this list itself when the other ranges hold every address and this list does not.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipRanges"/> is <see langword="null"/>.</exception>
    public IpRangeOrderedList Intersect(IEnumerable<IpRange> ipRanges)
    {
        return Intersect(ipRanges.ToOrderedList());
    }

    /// <summary>Returns a list of the addresses that this list and another both hold.</summary>
    /// <param name="ipRanges">The other list.</param>
    /// <returns>
    /// The new list; <paramref name="ipRanges"/> itself when this list holds every address, or else this list itself
    /// when <paramref name="ipRanges"/> does.
    /// </returns>
    public IpRangeOrderedList Intersect(IpRangeOrderedList ipRanges)
    {
        return Intersect(this, ipRanges);
    }

    /// <summary>Returns a list of the addresses this list does not hold.</summary>
    /// <param name="includeIPv4">
    /// <see langword="true"/> to include the IPv4 addresses outside this list; <see langword="false"/> to leave IPv4
    /// out of the result.
    /// </param>
    /// <param name="includeIPv6">
    /// <see langword="true"/> to include the IPv6 addresses outside this list; <see langword="false"/> to leave IPv6
    /// out of the result.
    /// </param>
    /// <returns>The new list. A family this list holds no range of comes back whole, when included.</returns>
    /// <exception cref="InvalidOperationException">
    /// A range's ends would compare out of order, which happens only with IPv4-mapped addresses: see the remarks of
    /// <see cref="IpRangeOrderedList"/>.
    /// </exception>
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
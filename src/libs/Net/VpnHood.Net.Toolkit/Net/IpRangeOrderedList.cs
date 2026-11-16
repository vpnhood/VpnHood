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
/// where nothing changes. The ranges are in the order of their first addresses, the IPv4 ranges before the IPv6 ones,
/// and <see cref="Contains(IpAddressValue)"/> finds an address by binary search.
/// </para>
/// <para>
/// An IPv4-mapped IPv6 address is an IPv4 address in IPv6's form: <see cref="IpRange"/> makes a range of such
/// addresses the range of their IPv4 addresses, and <see cref="Contains(IpAddressValue)"/> asks about one as its IPv4
/// address. A range that only reaches into the mapped addresses, as that of <c>::/80</c> does, is a range of IPv6
/// addresses like any other, which holds none of them for a lookup. So two IPv6 ranges with nothing between them but
/// mapped addresses are one range, and what an intersection would leave wholly among them is left out.
/// </para>
/// </remarks>
public class IpRangeOrderedList :
    IOrderedEnumerable<IpRange>,
    IReadOnlyList<IpRange>
{
    // the IPv6 addresses on both sides of the IPv4-mapped ones, ::ffff:0:0/96
    private static readonly IPAddress LastBeforeMapped = IPAddress.Parse("::fffe:ffff:ffff");
    private static readonly IPAddress FirstAfterMapped = IPAddress.Parse("::1:0:0:0");

    private readonly List<IpRange> _orderedList;

    // the ranges' ends as values, for a search that makes no object (see BuildSearchRanges)
    private readonly IpAddressValue[] _searchFirsts;
    private readonly IpAddressValue[] _searchLasts;

    /// <summary>Adds a key to the list's order, for <c>ThenBy</c> and <c>ThenByDescending</c>.</summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="keySelector">The function that gets a range's key.</param>
    /// <param name="comparer">The comparer of keys, or <see langword="null"/> for the default one.</param>
    /// <param name="descending">
    /// <see langword="true"/> to sort by the key in descending order; <see langword="false"/> for ascending.
    /// </param>
    /// <returns>The ranges, in this list's order, then the key's.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// As with any ordered sequence, this list's order comes first, and the key orders only what it leaves equal;
    /// the list's ranges never start at the same address, so <c>list.ThenBy(key)</c> keeps the list's order.
    /// </remarks>
    public IOrderedEnumerable<IpRange> CreateOrderedEnumerable<TKey>(
        Func<IpRange, TKey> keySelector,
        IComparer<TKey>? comparer, bool descending) =>
        // this list's order first, as its first addresses give it, and the key for what that leaves equal
        _orderedList
            .OrderBy(x => x.FirstIpAddress.ToValue())
            .CreateOrderedEnumerable(keySelector, comparer, descending);

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

    // orderedList must be sorted and merged already. Inside this class C# takes this constructor, not the public
    // one, for anything declared a List: pass one only from Sort, or from an operation that leaves the ranges so.
    private IpRangeOrderedList(List<IpRange> orderedList)
    {
        _orderedList = orderedList;
        BuildSearchRanges(orderedList, out _searchFirsts, out _searchLasts);
    }

    /// <summary>Writes the ranges to a stream in a compact binary form, which <see cref="Deserialize"/> reads.</summary>
    /// <param name="stream">The stream, which is left open.</param>
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
        // serialize to binary; the stream stays the caller's
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
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
    /// <param name="stream">The stream, at the start of the data. It is left open.</param>
    /// <returns>The list, sorted and merged again.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="EndOfStreamException">The stream ends before the last range's end.</exception>
    /// <exception cref="InvalidDataException">
    /// The number of ranges is negative, or an address is neither 4 nor 16 bytes long.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="stream"/> cannot be read, or a range's addresses are of different families, or out of order.
    /// </exception>
    public static IpRangeOrderedList Deserialize(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var length = ReadCount(reader);

        // Sorted and merged again, as the bytes may be anyone's. The ranges are read as the list takes them, so no
        // room is made for a count the stream only claims.
        return new IpRangeOrderedList(ReadRanges(reader, length));
    }

    private static IEnumerable<IpRange> ReadRanges(BinaryReader reader, int count)
    {
        for (var i = 0; i < count; i++) {
            var firstIpBytes = ReadAddress(reader);
            var lastIpBytes = ReadAddress(reader);
            yield return new IpRange(new IPAddress(firstIpBytes), new IPAddress(lastIpBytes));
        }
    }

    private static int ReadCount(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        return count >= 0 ? count : throw new InvalidDataException("The number of IP ranges is negative.");
    }

    // An address of 4 or 16 bytes, whole: read as its length says and no further check, a last address that the
    // stream's end cut to 4 of its 16 bytes was an IPv4 address.
    private static byte[] ReadAddress(BinaryReader reader)
    {
        var length = reader.ReadByte();
        if (length is not (4 or 16))
            throw new InvalidDataException($"An IP address of {length} bytes is neither IPv4 nor IPv6.");

        var bytes = reader.ReadBytes(length);
        return bytes.Length == length ? bytes : throw new EndOfStreamException("The IP ranges end inside an address.");
    }

    /// <summary>
    /// Reads ranges that <see cref="Serialize"/> wrote as their addresses' bytes, with no <see cref="IpRange"/> or
    /// <see cref="IPAddress"/> made, and no sorting.
    /// </summary>
    /// <param name="stream">The stream, at the start of the data. It is left open.</param>
    /// <returns>
    /// Each range's first and last address bytes, 4 for IPv4 and 16 for IPv6, in the stream's order, which
    /// <see cref="Serialize"/> wrote sorted and merged. Nothing is read until the result is enumerated.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> is <see langword="null"/>, when the enumeration starts.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot be read, when the enumeration starts.</exception>
    /// <exception cref="EndOfStreamException">The stream ends before the last range's end.</exception>
    /// <exception cref="InvalidDataException">
    /// The number of ranges is negative, or an address is neither 4 nor 16 bytes long.
    /// </exception>
    /// <remarks>For a caller that only stores the ranges, such as a bulk insert into a database.</remarks>
    public static IEnumerable<(byte[] Start, byte[] End)> DeserializeRaw(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var length = ReadCount(reader);
        for (var i = 0; i < length; i++) {
            var firstIpBytes = ReadAddress(reader);
            var lastIpBytes = ReadAddress(reader);
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
    /// The address. An IPv4-mapped IPv6 address is looked up as its IPv4 address, and a scope id is ignored.
    /// </param>
    /// <returns><see langword="true"/> if a range holds the address; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipAddress"/> is <see langword="null"/>.</exception>
    public bool Contains(IPAddress ipAddress) => Contains(ipAddress.ToValue());

    /// <summary>Determines whether an address is in one of the ranges, with no object made.</summary>
    /// <param name="ipAddress">
    /// The address. An IPv4-mapped IPv6 address is looked up as its IPv4 address, and a scope id is ignored; the
    /// default value is in no range.
    /// </param>
    /// <returns><see langword="true"/> if a range holds the address; otherwise, <see langword="false"/>.</returns>
    public bool Contains(IpAddressValue ipAddress)
    {
        var value = ipAddress.IsIPv4MappedToIPv6 ? ipAddress.MapToIPv4() : ipAddress;

        // the last range that starts at or before the address is the only one that may hold it
        var low = 0;
        var high = _searchFirsts.Length - 1;
        while (low <= high) {
            var middle = low + (high - low) / 2;
            if (_searchFirsts[middle].CompareAddress(value) <= 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return high >= 0 && value.CompareAddress(_searchLasts[high]) <= 0;
    }

    /// <summary>Determines whether the list holds no range.</summary>
    /// <returns><see langword="true"/> if it holds none; otherwise, <see langword="false"/>.</returns>
    public bool IsNone()
    {
        return _orderedList.Count == 0;
    }

    /// <summary>Converts each range to the fewest networks that hold its addresses.</summary>
    /// <returns>The networks, in the order of their first addresses, the IPv4 networks before the IPv6 ones.</returns>
    public IOrderedEnumerable<IpNetwork> ToIpNetworks()
    {
        var ipNetworkList = new List<IpNetwork>();
        foreach (var ipRange in _orderedList)
            ipNetworkList.AddRange(ipRange.ToIpNetworks());

        return ipNetworkList.OrderBy(x => x.FirstIpAddress.ToValue());
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
        // read once: a sequence that can be read only once, or costs to read, would give its ranges to the first look
        var addedRanges = ipRanges as IReadOnlyCollection<IpRange> ?? ipRanges.ToArray();
        return addedRanges.Count > 0 ? new IpRangeOrderedList(_orderedList.Concat(addedRanges)) : this;
    }

    /// <summary>Returns a list of this list's addresses but one.</summary>
    /// <param name="ipAddress">The address to leave out.</param>
    /// <returns>The new list.</returns>
    public IpRangeOrderedList Exclude(IPAddress ipAddress)
    {
        return Exclude(new IpRange(ipAddress));
    }

    /// <summary>Returns a list of this list's addresses outside a range.</summary>
    /// <param name="ipRange">The range to leave out.</param>
    /// <returns>The new list.</returns>
    public IpRangeOrderedList Exclude(IpRange ipRange)
    {
        return Exclude([ipRange]);
    }

    /// <summary>Returns a list of this list's addresses outside other ranges.</summary>
    /// <param name="ipRanges">The ranges to leave out, in any order.</param>
    /// <returns>The new list, which may be this list itself when <paramref name="ipRanges"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipRanges"/> is <see langword="null"/>.</exception>
    public IpRangeOrderedList Exclude(IEnumerable<IpRange> ipRanges)
    {
        return Exclude(ipRanges.ToOrderedList());
    }

    /// <summary>Returns a list of this list's addresses outside another list.</summary>
    /// <param name="ipRanges">The ranges to leave out.</param>
    /// <returns>The new list, which may be this list itself when <paramref name="ipRanges"/> is empty.</returns>
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
    public IpRangeOrderedList Invert(bool includeIPv4 = true, bool includeIPv6 = true)
    {
        return Invert(this, includeIPv4: includeIPv4, includeIPv6: includeIPv6);
    }

    // In the order of the ranges' own ends, by number: IPAddressUtil.Compare, which takes an IPv4-mapped address as
    // its IPv4 address, would put a range that starts among the mapped addresses with the IPv4 ranges.
    private static List<IpRange> Sort(IEnumerable<IpRange> ipRanges)
    {
        var sortedRanges = ipRanges.OrderBy(x => x.FirstIpAddress.ToValue());
        return Unify(sortedRanges);
    }

    private static List<IpRange> Unify(IEnumerable<IpRange> sortedIpRanges)
    {
        List<IpRange> res = [];
        foreach (var ipRange in sortedIpRanges) {
            if (res.Count > 0 && IsOneRange(res[^1], ipRange)) {
                if (IPAddressUtil.CompareUnmapped(ipRange.LastIpAddress, res[^1].LastIpAddress) > 0)
                    res[^1] = new IpRange(res[^1].FirstIpAddress, ipRange.LastIpAddress);
            }
            else {
                res.Add(ipRange);
            }
        }

        return res;
    }

    // Whether a range and the next one in the order are one range: they overlap or touch, or nothing lies between
    // them but IPv4-mapped addresses, which no lookup asks about. Kept apart, the two ranges around the mapped
    // addresses would hold every IPv6 address a lookup can ask about and yet not be all of IPv6, and the gap between
    // them, inverted, would be a range IpRange takes for every IPv4 address.
    private static bool IsOneRange(IpRange ipRange, IpRange nextIpRange)
    {
        if (ipRange.AddressFamily != nextIpRange.AddressFamily)
            return false;

        if (IPAddressUtil.CompareUnmapped(IPAddressUtil.Decrement(nextIpRange.FirstIpAddress), ipRange.LastIpAddress) <= 0)
            return true;

        return ipRange.AddressFamily == AddressFamily.InterNetworkV6 &&
               IPAddressUtil.CompareUnmapped(ipRange.LastIpAddress, LastBeforeMapped) >= 0 &&
               IPAddressUtil.CompareUnmapped(nextIpRange.FirstIpAddress, FirstAfterMapped) <= 0;
    }

    // What two ranges share. A piece wholly among the IPv4-mapped addresses, as ::/80 and a range that starts at the
    // first of them share, is left out: it holds no address a lookup asks about, and IpRange would make it the IPv4
    // range it is not.
    private static void AddUnlessMapped(List<IpRange> ipRanges, IPAddress firstIpAddress, IPAddress lastIpAddress)
    {
        if (firstIpAddress.IsIPv4MappedToIPv6 && lastIpAddress.IsIPv4MappedToIPv6)
            return;

        ipRanges.Add(new IpRange(firstIpAddress, lastIpAddress));
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
        // what two ranges share runs from the higher of their first addresses to the lower of their last ones
        var ipRanges = new List<IpRange>();
        foreach (var ipRange1 in orderedIpRanges1)
        foreach (var ipRange2 in orderedIpRanges2) {
            var first = IPAddressUtil.CompareUnmapped(ipRange1.FirstIpAddress, ipRange2.FirstIpAddress) >= 0
                ? ipRange1.FirstIpAddress
                : ipRange2.FirstIpAddress;
            var last = IPAddressUtil.CompareUnmapped(ipRange1.LastIpAddress, ipRange2.LastIpAddress) <= 0
                ? ipRange1.LastIpAddress
                : ipRange2.LastIpAddress;

            if (IPAddressUtil.CompareUnmapped(first, last) <= 0)
                AddUnlessMapped(ipRanges, first, last);
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

            // never a gap of IPv4-mapped addresses alone, which IpRange would take for IPv4: the list holds the two
            // ranges around such a gap as one
            if (i > 0)
                res.Add(new IpRange(IPAddressUtil.Increment(orderedIpRanges[i - 1].LastIpAddress),
                    IPAddressUtil.Decrement(ipRange.FirstIpAddress)));

            if (i == orderedIpRanges.Length - 1 && !IPAddressUtil.IsMaxValue(ipRange.LastIpAddress))
                res.Add(new IpRange(IPAddressUtil.Increment(ipRange.LastIpAddress), maxIpValue));
        }

        return res;
    }

    // The ranges are sorted and no two of them overlap, so the last range that starts at or before an address is the
    // only one that may hold it.
    private static void BuildSearchRanges(List<IpRange> orderedList,
        out IpAddressValue[] firsts, out IpAddressValue[] lasts)
    {
        firsts = new IpAddressValue[orderedList.Count];
        lasts = new IpAddressValue[orderedList.Count];
        for (var i = 0; i < orderedList.Count; i++) {
            firsts[i] = orderedList[i].FirstIpAddress.ToValue();
            lasts[i] = orderedList[i].LastIpAddress.ToValue();
        }
    }
}
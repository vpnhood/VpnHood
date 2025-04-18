﻿namespace VpnHood.Core.Toolkit.Net;

public static class IpRangeExtension
{
    public static IpRangeOrderedList ToOrderedList(this IEnumerable<IpRange> ipRanges)
    {
        return new IpRangeOrderedList(ipRanges);
    }

    public static IEnumerable<IpRange> Intersect(this IEnumerable<IpRange> first, IEnumerable<IpRange> second)
    {
        // prevent use Linq.Intersect in mistake. it is bug prone.
        throw new NotSupportedException($"Use {nameof(IpRangeOrderedList)}.Intersect.");
    }

    public static string ToText(this IEnumerable<IpRange> ipRanges)
    {
        return string.Join(Environment.NewLine, ipRanges.Select(x => x.ToString()));
    }
}
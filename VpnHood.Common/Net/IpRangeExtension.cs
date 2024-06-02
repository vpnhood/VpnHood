namespace VpnHood.Common.Net;

public static class IpRangeExtension
{
    public static IpRangeOrderedList ToOrderedList(this IEnumerable<IpRange> ipRanges)
    {
        return new IpRangeOrderedList(ipRanges);
    }
}
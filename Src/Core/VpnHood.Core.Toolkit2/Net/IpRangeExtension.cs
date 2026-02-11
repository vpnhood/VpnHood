namespace VpnHood.Core.Toolkit.Net;

public static class IpRangeExtension
{
    extension(IEnumerable<IpRange> ipRanges)
    {
        public IpRangeOrderedList ToOrderedList()
        {
            return new IpRangeOrderedList(ipRanges);
        }

        public IEnumerable<IpRange> Intersect(IEnumerable<IpRange> second)
        {
            _ = second;

            // prevent use Linq.Intersect in mistake. it is bug prone.
            throw new NotSupportedException($"Use {nameof(IpRangeOrderedList)}.Intersect.");
        }

        public string ToText()
        {
            return string.Join(Environment.NewLine, ipRanges.Select(x => x.ToString()));
        }
    }
}
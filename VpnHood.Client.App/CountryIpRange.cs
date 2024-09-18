using VpnHood.Common.Net;

namespace VpnHood.Client.App;

public class CountryIpRange
{
    public required string CountryCode { get; init; }
    public required IpRangeOrderedList IpRanges { get; init; }
}
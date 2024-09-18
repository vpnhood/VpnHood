using System.Globalization;
using VpnHood.Common.Net;

namespace VpnHood.Client.App;

public class CountryIpRange
{
    public string CountryName => new RegionInfo(CountryCode).EnglishName;
    public required string CountryCode { get; init; }
    public required IpRangeOrderedList IpRanges { get; init; }
}
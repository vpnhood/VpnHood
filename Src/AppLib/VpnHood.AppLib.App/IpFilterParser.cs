using System.Text.RegularExpressions;
using VpnHood.Core.Common.Net;

namespace VpnHood.AppLib;

public static class IpFilterParser
{
    public static IpRange[]? Parse(string? ipFilter)
    {
        if (string.IsNullOrWhiteSpace(ipFilter))
            return null;

        // remove all comments which start with # or ;
        ipFilter = Regex.Replace(ipFilter, @"#.*|;.*", string.Empty);

        // split by new line
        var lines = ipFilter
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        // parse each line
        var ipRanges = lines
            .Select(x=>IpRange.Parse(x.Trim())).ToArray();
        return ipRanges;
    }

    public static IpRange[] Parse(string? ipFilter, IpRange[] defaultValue)
    {
        var ipRanges = Parse(ipFilter);
        return ipRanges is null || ipRanges.Length == 0 ? defaultValue : ipRanges;
    }


    public static IpRange[] ParseIncludes(string? ipFilter)
    {
        return Parse(ipFilter, IpNetwork.All.ToIpRanges().ToArray());
    }

    public static IpRange[] ParseExcludes(string? ipFilter)
    {
        return Parse(ipFilter, IpNetwork.None.ToIpRanges().ToArray());
    }
}
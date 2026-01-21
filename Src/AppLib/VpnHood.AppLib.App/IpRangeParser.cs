using System.Text.RegularExpressions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.AppLib;

public static class IpRangeParser
{
    public static IpRange[]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // remove all comments which start with # or ;
        text = Regex.Replace(text, @"#.*|;.*", string.Empty);

        // split by new line
        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        // parse each line
        var ipRanges = lines
            .Select(x => IpRange.Parse(x.Trim())).ToArray();
        return ipRanges;
    }

    public static IpRange[] Parse(string? text, IpRange[] defaultValue)
    {
        var ipRanges = Parse(text);
        return ipRanges is null || ipRanges.Length == 0 ? defaultValue : ipRanges;
    }


    public static IpRange[] ParseIncludes(string? text)
    {
        return Parse(text, IpNetwork.All.ToIpRanges().ToArray());
    }

    public static IpRange[] ParseExcludes(string? text)
    {
        return Parse(text, IpNetwork.None.ToIpRanges().ToArray());
    }
}
using System.Text.RegularExpressions;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.AppLib.App.Utils;

internal static class IpRangeTextFileParser
{
    public static IpRange[]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return GetEntries(text).Select(IpRange.Parse).ToArray();
    }

    public static IpRange[] Parse(string? text, IpRange[] defaultValue)
    {
        var ipRanges = Parse(text);
        return ipRanges is null || ipRanges.Length == 0 ? defaultValue : ipRanges;
    }

    public static IpRange[] ParseIncludes(string? text)
    {
        return Parse(text, [.. IpNetwork.All.ToIpRanges()]);
    }

    public static IpRange[] ParseExcludes(string? text)
    {
        return Parse(text, [.. IpNetwork.None.ToIpRanges()]);
    }

    // Refuses (SplitEntryException) the first entry that is no address, range or network, by name, so the UI can say
    // which in the person's language.
    public static void Verify(string? text)
    {
        foreach (var entry in GetEntries(text ?? "")) {
            try {
                IpRange.Parse(entry);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException) {
                throw SplitEntryException.Create(SplitEntryError.InvalidIpRange, entry, ex);
            }
        }
    }

    // A list's entries, one a line, without comments (# or ;); a line that holds nothing else, an indented comment's
    // too, is no entry
    private static IEnumerable<string> GetEntries(string text)
    {
        return Regex.Replace(text, @"#.*|;.*", string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0);
    }
}

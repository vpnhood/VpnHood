using System.Data;
using System.Text.RegularExpressions;

namespace VpnHood.Manager.Common.Utils;

public static class ClientFilterUtils
{
    private static readonly DataTable DataTable = new();
    public static void Validate(string filter, string[] tags)
    {
        if (string.IsNullOrWhiteSpace(filter))
            throw new ArgumentException("Filter can not be empty.", nameof(filter));

        Verify(filter, tags);
    }

    // replace shorthand operators with full operators
    private static string Normalize(string filter)
    {
        return filter
            .Replace("&&", "AND")
            .Replace("&", "AND")
            .Replace("||", "OR")
            .Replace("|", "OR")
            .Replace("!", "NOT ");
    }

    public static bool Verify(string filter, IEnumerable<string> tags)
    {
        filter = Normalize(filter);

        // regex to match "tag_xxx" pattern
        filter = Regex.Replace(filter, @"#[\w:]+", match =>
            tags.Contains(match.Value, StringComparer.OrdinalIgnoreCase)
                .ToString());

        return DataTable.Compute(filter, string.Empty) is true;
    }
}
using System.Text.RegularExpressions;

namespace VpnHood.AppLib;

public static class DomainTextFileParser
{
    public static string[]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // remove all comments which start with # or ;
        text = Regex.Replace(text, @"#.*|;.*", string.Empty);

        // split by new line
        var domains = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();

        return domains;
    }

    public static string[] Parse(string? text, string[] defaultValue)
    {
        var domains = Parse(text);
        return domains is null || domains.Length == 0 ? defaultValue : domains;
    }
}

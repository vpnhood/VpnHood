using System.Text.RegularExpressions;

namespace VpnHood.AppLib.App.Utils;

internal static class DomainTextFileParser
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

    // Refuses (FormatException) an entry with a *. An entry matches its domain and every subdomain already, so a leading
    // "*." changes nothing, and the filters take any other * as a letter no name has: the entry would match nothing
    // while it looks like a rule. Only a save checks: a list stored before, or edited by hand, is built as it is at the
    // connect, where the filters still read a leading "*." as before. Failing there would stop always on, and leave a
    // switch turned on while connected with none of the list's rules, as a live change's failure is only logged.
    public static void Verify(string? text)
    {
        foreach (var domain in Parse(text) ?? []) {
            if (domain.Contains('*'))
                throw new FormatException($"A domain takes no *, as example.com covers all its subdomains already: {domain}");
        }
    }
}

using System.Text.RegularExpressions;
using VpnHood.AppLogic.Api.Exceptions;

namespace VpnHood.AppLogic.App.Utils;

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

    // Refuses (SplitEntryException) an entry with a *, by name, so the UI can say which in the person's language. An
    // entry matches its domain and every subdomain already, so a leading "*." changes nothing, and the filters take any
    // other * as a letter no name has: the entry would match nothing while it looks like a rule. Only a save checks: a
    // list stored before, or edited by hand, is built as it is at the connect, where the filters still read a leading
    // "*." as before. Failing there would stop always on, and leave a switch turned on while connected with none of
    // the list's rules, as a live change's failure is only logged.
    public static void Verify(string? text)
    {
        foreach (var domain in Parse(text) ?? []) {
            if (domain.Contains('*'))
                throw SplitEntryException.Create(SplitEntryError.Asterisk, domain);
        }
    }

    // A list stored before the lists took no *, or edited by hand, as it is read: an entry's leading "*." goes, as it
    // meant what the domain alone means, so the person never meets a refusal for it at the next save. Only where that
    // keeps what the entry matches: an entry with another *, as "*.*.example.com", or with white space after its "*.",
    // as "*. example.com", matched nothing, and without the "*." it would match example.com. Such an entry is left
    // for the save to name. Lines end as Parse reads them: at \r\n, \r or \n.
    public static string RemoveLeadingWildcards(string text)
    {
        // the line breaks stay in the array, at the odd places, as the split's pattern captures them
        var parts = Regex.Split(text, @"(\r\n|\r|\n)");
        for (var i = 0; i < parts.Length; i += 2)
            parts[i] = RemoveLeadingWildcard(parts[i]);

        return string.Concat(parts);
    }

    private static string RemoveLeadingWildcard(string line)
    {
        var entry = line.TrimStart();
        if (!entry.StartsWith("*.", StringComparison.Ordinal))
            return line;

        // the domain alone, before a comment; a bare "*." was no entry, and stays none without it
        var domain = entry[2..];
        var commentIndex = domain.IndexOfAny(['#', ';']);
        var name = (commentIndex < 0 ? domain : domain[..commentIndex]).TrimEnd();
        if (name.Length > 0 && (char.IsWhiteSpace(name[0]) || name.Contains('*')))
            return line;

        return line[..(line.Length - entry.Length)] + domain;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace VpnHood.Net.VpnAdapters.LinuxTun;

// A link's DNS domains as systemd-resolved has them, in the words of `resolvectl domain`: a search domain as it is,
// a routing domain with a leading ~. The same line is what the adapter records of a link whose domains it takes off,
// so one reader serves both; the record alone marks a link that systemd-networkd managed when they were taken, which
// decides how they go back (LinuxLinkDomainsKeeper).
internal sealed partial record LinuxLinkDomains(int Index, string Name, IReadOnlyList<string> Domains)
{
    private const string LinkWord = "Link";
    private const string ManagedLinkWord = "ManagedLink";

    public bool IsManagedByNetworkd { get; init; }

    // The lines of `resolvectl domain`, or of the adapter's record. A line that is no link's, as "Global:", is passed
    // over; a link's line that cannot be read is refused, as a link passed over would keep its domains unseen.
    public static IReadOnlyList<LinuxLinkDomains> ParseAll(string text)
    {
        return text.Split('\n')
            .Select(x => x.TrimEnd('\r'))
            .Where(x => x.StartsWith($"{LinkWord} ", StringComparison.Ordinal) ||
                        x.StartsWith($"{ManagedLinkWord} ", StringComparison.Ordinal))
            .Select(Parse)
            .ToArray();
    }

    // "Link 2 (eth0): lan ~corp.example"; a link with no domain ends at its colon
    public string ToLine()
    {
        var word = IsManagedByNetworkd ? ManagedLinkWord : LinkWord;
        return $"{word} {Index} ({Name}):{string.Concat(Domains.Select(x => $" {x}"))}";
    }

    private static LinuxLinkDomains Parse(string line)
    {
        var match = LinkRegex().Match(line);
        if (!match.Success ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index <= 0)
            throw new FormatException($"'{line}' is not a link's line as resolvectl writes one.");

        // Refused, not left out: a domain goes onto root's command line when it is given back, and one that is
        // passed over is a domain lost
        var domains = match.Groups[4].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var unexpected = domains.FirstOrDefault(x => !DomainRegex().IsMatch(x));
        if (unexpected != null)
            throw new FormatException($"'{unexpected}' is not a DNS domain as resolvectl writes one.");

        return new LinuxLinkDomains(index, Name: match.Groups[3].Value, domains) {
            IsManagedByNetworkd = match.Groups[1].Value == ManagedLinkWord
        };
    }

    // a name holds no colon, so the first "):" ends it
    [GeneratedRegex(@"^(Link|ManagedLink) (\d+) \((.+?)\):(.*)$")]
    private static partial Regex LinkRegex();

    // As systemd writes a domain: letters, digits, _ and -, the dots between its labels, and a backslash before
    // anything else, by itself or as three digits. None of them ends a single-quoted word on a command line.
    [GeneratedRegex(@"^~?[A-Za-z0-9_.\\-]+$")]
    private static partial Regex DomainRegex();
}

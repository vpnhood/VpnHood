namespace VpnHood.Core.DomainFiltering;

public class DomainFilterResolver(DomainFilter domainFilter)
{
    public DomainFilterAction DefaultAction { get; } = BuildDefaultAction(domainFilter);

    private static DomainFilterAction BuildDefaultAction(DomainFilter domainFilter)
    {
        return domainFilter.Includes.Any()
            ? DomainFilterAction.Exclude  // Default to exclude if includes are specified
            : DomainFilterAction.Include;
    }

    public DomainFilterAction Process(string? domain)
    {
        var topDomains = ExtractTopDomains(domain);
        foreach (var topDomain in topDomains) {

            if (IsMatch(topDomain, domainFilter.Blocks))
                return DomainFilterAction.Block;

            if (IsMatch(topDomain, domainFilter.Excludes))
                return DomainFilterAction.Exclude;

            if (IsMatch(topDomain, domainFilter.Includes))
                return DomainFilterAction.Include;
        }

        return DefaultAction;
    }

    private static bool IsMatch(string domain, string[] domains)
    {
        return domains.Contains(domain);
    }

    private static IEnumerable<string> ExtractTopDomains(string? domain)
    {
        if (string.IsNullOrEmpty(domain))
            return [];

        var topDomains = new List<string>();
        var parts = domain.Split('.');
        for (var i = 0; i < parts.Length; i++) {
            var topDomain = string.Join('.', parts.Skip(i));
            topDomains.Add(topDomain);
        }

        return topDomains;
    }
}
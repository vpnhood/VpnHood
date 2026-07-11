namespace VpnHood.Core.Filtering.Abstractions;

// Canonical domain-name form shared by every domain filter and the split-domain db builder: entries and
// queries must normalize/invert identically or matching silently breaks across implementations.
public static class DomainUtils
{
    // Trimmed, lower-case, leading wildcard stripped ("*.example.com" → "example.com" — every entry matches
    // itself and its subdomains anyway). An empty result means "no domain" and can never match.
    public static string NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return string.Empty;

        var normalized = domain.Trim().ToLowerInvariant();
        if (normalized.StartsWith("*.", StringComparison.Ordinal))
            normalized = normalized[2..];

        return normalized;
    }

    // Inverts domain parts: "www.google.com" -> "com.google.www". In this form an ancestor domain is an
    // ordinal prefix of its subdomains (at a '.' boundary), which is what the filters' lookups rely on.
    public static string InvertDomain(string domain)
    {
        var parts = domain.Split('.');
        Array.Reverse(parts);
        return string.Join('.', parts);
    }
}

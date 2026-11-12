namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Provides the form of domain names that every domain filter and the domain-list database builder share, so that
/// their entries and the names asked for match.
/// </summary>
public static class DomainUtils
{
    /// <summary>
    /// Normalizes a domain name: trimmed, in lower case, and without a leading <c>*.</c>, as an entry matches its
    /// subdomains anyway.
    /// </summary>
    /// <param name="domain">The name, or <see langword="null"/>.</param>
    /// <returns>
    /// The normalized name; empty for a null, empty or white-space name, which matches nothing.
    /// </returns>
    public static string NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return string.Empty;

        var normalized = domain.Trim().ToLowerInvariant();
        if (normalized.StartsWith("*.", StringComparison.Ordinal))
            normalized = normalized[2..];

        return normalized;
    }

    /// <summary>
    /// Reverses the order of a domain name's labels: <c>www.example.com</c> becomes <c>com.example.www</c>, a form in
    /// which a domain is an ordinal prefix of its subdomains, ending at a dot.
    /// </summary>
    /// <param name="domain">The name, as <see cref="NormalizeDomain"/> gives it.</param>
    /// <returns>The name with its labels reversed.</returns>
    public static string InvertDomain(string domain)
    {
        var parts = domain.Split('.');
        Array.Reverse(parts);
        return string.Join('.', parts);
    }
}

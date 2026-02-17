namespace VpnHood.Core.Filtering.Abstractions;

public class StaticDomainFilter(IDomainFilter? nextFilter) : IDomainFilter
{
    private string[] _invertedBlocks = [];
    private string[] _invertedExcludes = [];
    private string[] _invertedIncludes = [];

    public bool IsEmpty =>
        Blocks.Length == 0 &&
        Excludes.Length == 0 &&
        Includes.Length == 0;

    public string[] Blocks {
        get;
        set {
            field = value;
            _invertedBlocks = BuildSortedInvertedArray(value);
        }
    } = [];

    public string[] Excludes {
        get;
        set {
            field = value;
            _invertedExcludes = BuildSortedInvertedArray(value);
        }
    } = [];

    public string[] Includes {
        get;
        set {
            field = value;
            _invertedIncludes = BuildSortedInvertedArray(value);
        }
    } = [];

    private static string[] BuildSortedInvertedArray(string[] domains)
    {
        var inverted = domains
            .Select(NormalizeDomain)
            .Where(d => d.Length > 0)
            .Select(InvertDomain)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToArray();

        return inverted;
    }

    private static string NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return string.Empty;

        var normalized = domain.Trim().ToLowerInvariant();
        if (normalized.StartsWith("*.", StringComparison.Ordinal))
            normalized = normalized[2..];

        return normalized;
    }

    // Inverts domain parts: "www.google.com" -> "com.google.www"
    private static string InvertDomain(string domain)
    {
        var parts = domain.Split('.');
        Array.Reverse(parts);
        return string.Join('.', parts);
    }

    // it accepts wildcard such as *.example.com, which will match example.com, sub.example.com, etc.
    public FilterAction Process(string? domain)
    {
        var normalizedDomain = NormalizeDomain(domain);
        if (normalizedDomain.Length == 0)
            return FilterAction.Default;

        var invertedDomain = InvertDomain(normalizedDomain);

        if (IsMatch(invertedDomain, _invertedBlocks))
            return FilterAction.Block;

        if (IsMatch(invertedDomain, _invertedExcludes))
            return FilterAction.Exclude;

        if (IsMatch(invertedDomain, _invertedIncludes))
            return FilterAction.Include;

        return nextFilter?.Process(domain) ?? FilterAction.Default;
    }

    // Binary search to find exact match or prefix match (for wildcard support)
    private static bool IsMatch(string invertedDomain, string[] sortedInvertedDomains)
    {
        if (sortedInvertedDomains.Length == 0)
            return false;

        // Binary search for the position where invertedDomain would be inserted
        var index = Array.BinarySearch(sortedInvertedDomains, invertedDomain, StringComparer.Ordinal);

        // Exact match found
        if (index >= 0)
            return true;

        // Get the insertion point
        var insertionPoint = ~index;

        // Check the element before insertion point - it could be a prefix match
        // e.g., "com.google" matches "com.google.www"
        if (insertionPoint > 0) {
            var candidate = sortedInvertedDomains[insertionPoint - 1];
            if (IsWildcardMatch(invertedDomain, candidate))
                return true;
        }

        return false;
    }

    // Checks if invertedDomain starts with candidate followed by a dot
    // e.g., "com.google.www" matches pattern "com.google" (which represents *.google.com)
    private static bool IsWildcardMatch(string invertedDomain, string candidate)
    {
        return invertedDomain.Length > candidate.Length &&
               invertedDomain[candidate.Length] == '.' &&
               invertedDomain.AsSpan(0, candidate.Length).SequenceEqual(candidate.AsSpan());
    }

    public void Dispose()
    {
    }
}
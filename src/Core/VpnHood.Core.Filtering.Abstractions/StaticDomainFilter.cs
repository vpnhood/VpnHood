namespace VpnHood.Core.Filtering.Abstractions;

public class StaticDomainFilter : IDomainFilter
{
    private readonly IDomainFilter? _nextFilter;
    private readonly bool _autoDisposeNextFilter;
    private string[] _invertedBlocks = [];
    private string[] _invertedExcludes = [];
    private string[] _invertedIncludes = [];

    public event EventHandler? Changed;

    public StaticDomainFilter(IDomainFilter? nextFilter, bool autoDisposeNextFilter = true)
    {
        _nextFilter = nextFilter;
        _autoDisposeNextFilter = autoDisposeNextFilter;

        // roll a change announced below this stage up the pipe (this stage's own lists are unaffected)
        if (nextFilter != null)
            nextFilter.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool IsEmpty =>
        Blocks.Count == 0 &&
        Excludes.Count == 0 &&
        Includes.Count == 0 &&
        (_nextFilter?.IsEmpty ?? true);

    // the setters raise Changed: filling a list later changes verdicts, so the caches above must drop
    // theirs and the endpoints re-check IsEmpty — same contract as a gate swap
    public IReadOnlyList<string> Blocks {
        get;
        set {
            field = value;
            _invertedBlocks = BuildSortedInvertedArray(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = [];

    public IReadOnlyList<string> Excludes {
        get;
        set {
            field = value;
            _invertedExcludes = BuildSortedInvertedArray(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = [];

    public IReadOnlyList<string> Includes {
        get;
        set {
            field = value;
            _invertedIncludes = BuildSortedInvertedArray(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = [];

    private static string[] BuildSortedInvertedArray(IReadOnlyList<string> domains)
    {
        var inverted = domains
            .Select(DomainUtils.NormalizeDomain)
            .Where(d => d.Length > 0)
            .Select(DomainUtils.InvertDomain)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToArray();

        return inverted;
    }

    // it accepts wildcard such as *.example.com, which will match example.com, sub.example.com, etc.
    public FilterAction Process(string? domain)
    {
        var normalizedDomain = DomainUtils.NormalizeDomain(domain);
        if (normalizedDomain.Length == 0)
            return FilterAction.Default;

        var invertedDomain = DomainUtils.InvertDomain(normalizedDomain);

        if (IsMatch(invertedDomain, _invertedBlocks))
            return FilterAction.Block;

        if (IsMatch(invertedDomain, _invertedExcludes))
            return FilterAction.Exclude;

        if (IsMatch(invertedDomain, _invertedIncludes))
            return FilterAction.Include;

        return _nextFilter?.Process(domain) ?? FilterAction.Default;
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

    // this stage's own lists are program state, not external configuration; just forward the command
    public void Reconfigure() => _nextFilter?.Reconfigure();

    public void Dispose()
    {
        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}
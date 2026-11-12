namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Provides a stage of a domain filter pipe that decides by three lists of domains held in memory: blocked,
/// excluded and included.
/// </summary>
/// <remarks>
/// An entry matches its domain and its subdomains, with or without a leading <c>*.</c>, and names and entries are
/// compared as <see cref="DomainUtils.NormalizeDomain"/> gives them; but if no exact match is found, a lookup tests
/// only the entry just before the name in the order of their reversed labels, so a listed parent is missed when that
/// entry is not itself a parent, as with <c>example-cdn.com</c> between <c>example.com</c> and <c>www.example.com</c>.
/// A name on the block list is blocked, one on the exclude list excluded, and one on the include list included, in that
/// order; any other name gets the next stage's verdict. Setting a list raises <see cref="Changed"/>.
/// </remarks>
public class StaticDomainFilter : IDomainFilter
{
    private readonly IDomainFilter? _nextFilter;
    private readonly bool _autoDisposeNextFilter;
    private string[] _invertedBlocks = [];
    private string[] _invertedExcludes = [];
    private string[] _invertedIncludes = [];

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticDomainFilter"/> class, with empty lists.
    /// </summary>
    /// <param name="nextFilter">
    /// The stage that decides the names no list matches, or <see langword="null"/> to give them
    /// <see cref="FilterAction.Default"/>.
    /// </param>
    /// <param name="autoDisposeNextFilter">
    /// <see langword="true"/> to dispose <paramref name="nextFilter"/> with this stage.
    /// </param>
    public StaticDomainFilter(IDomainFilter? nextFilter, bool autoDisposeNextFilter = true)
    {
        _nextFilter = nextFilter;
        _autoDisposeNextFilter = autoDisposeNextFilter;

        // roll a change announced below this stage up the pipe (this stage's own lists are unaffected)
        if (nextFilter != null)
            nextFilter.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    /// <value>
    /// <see langword="true"/> while the three lists are empty and the next stage, if any, is empty too.
    /// </value>
    public bool IsEmpty =>
        Blocks.Count == 0 &&
        Excludes.Count == 0 &&
        Includes.Count == 0 &&
        (_nextFilter?.IsEmpty ?? true);

    // the setters raise Changed: filling a list later changes verdicts, so the caches above must drop
    // theirs and the endpoints re-check IsEmpty — same contract as a gate swap
    /// <summary>Gets or sets the domains whose flows are blocked. The default is empty.</summary>
    public IReadOnlyList<string> Blocks {
        get;
        set {
            field = value;
            _invertedBlocks = BuildSortedInvertedArray(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = [];

    /// <summary>Gets or sets the domains whose flows go around the tunnel. The default is empty.</summary>
    public IReadOnlyList<string> Excludes {
        get;
        set {
            field = value;
            _invertedExcludes = BuildSortedInvertedArray(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = [];

    /// <summary>
    /// Gets or sets the domains whose flows go through the tunnel, whatever an IP filter says. The default is empty.
    /// </summary>
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

    /// <summary>Decides what happens to a flow for a domain name, by the lists, then by the next stage.</summary>
    /// <param name="domain">The name, or <see langword="null"/>.</param>
    /// <returns>
    /// The verdict of the first list that matches the name, or else the next stage's; <see cref="FilterAction.Default"/>
    /// for a null, empty or white-space name, which the next stage is not asked about.
    /// </returns>
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

    /// <inheritdoc/>
    /// <remarks>It passes the call to the next stage: its own lists are no outside configuration.</remarks>
    public Task Reconfigure(CancellationToken cancellationToken) =>
        _nextFilter?.Reconfigure(cancellationToken) ?? Task.CompletedTask;

    /// <summary>Disposes the next stage when this stage was made to.</summary>
    public void Dispose()
    {
        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}
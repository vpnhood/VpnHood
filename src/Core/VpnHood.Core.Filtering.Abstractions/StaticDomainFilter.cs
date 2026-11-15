namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Provides a stage of a domain filter pipe that decides by three lists of domains held in memory: blocked,
/// excluded and included.
/// </summary>
/// <remarks>
/// An entry matches its domain and its subdomains, with or without a leading <c>*.</c>, and names and entries are
/// compared as <see cref="DomainUtils.NormalizeDomain"/> gives them. A name on the block list is blocked, one on the
/// exclude list excluded, and one on the include list included, in that order; any other name gets the next stage's
/// verdict. Setting a list copies it and raises <see cref="Changed"/>.
/// </remarks>
public class StaticDomainFilter : IDomainFilter
{
    private readonly IDomainFilter? _nextFilter;
    private readonly bool _autoDisposeNextFilter;
    private HashSet<string> _blocks = [];
    private HashSet<string> _excludes = [];
    private HashSet<string> _includes = [];

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
        if (nextFilter != null)
            nextFilter.Changed += NextFilter_Changed;
    }

    // roll a change announced below this stage up the pipe (this stage's own lists are unaffected)
    private void NextFilter_Changed(object? sender, EventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
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

    // The setters raise Changed: filling a list later changes verdicts, so the caches above must drop
    // theirs and the endpoints re-check IsEmpty — same contract as a gate swap. They copy the list: kept as the
    // caller's own, a change made to it in place showed in IsEmpty and not in the verdicts
    /// <summary>Gets or sets the domains whose flows are blocked. The default is empty.</summary>
    /// <remarks>The setter copies the list.</remarks>
    public IReadOnlyList<string> Blocks {
        get;
        set {
            field = value.ToArray();
            _blocks = BuildNormalizedSet(field);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = [];

    /// <summary>Gets or sets the domains whose flows go around the tunnel. The default is empty.</summary>
    /// <remarks>The setter copies the list.</remarks>
    public IReadOnlyList<string> Excludes {
        get;
        set {
            field = value.ToArray();
            _excludes = BuildNormalizedSet(field);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = [];

    /// <summary>
    /// Gets or sets the domains whose flows go through the tunnel, whatever an IP filter says. The default is empty.
    /// </summary>
    /// <remarks>The setter copies the list.</remarks>
    public IReadOnlyList<string> Includes {
        get;
        set {
            field = value.ToArray();
            _includes = BuildNormalizedSet(field);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = [];

    private static HashSet<string> BuildNormalizedSet(IReadOnlyList<string> domains)
    {
        return domains
            .Select(DomainUtils.NormalizeDomain)
            .Where(d => d.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
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

        if (IsMatch(normalizedDomain, _blocks))
            return FilterAction.Block;

        if (IsMatch(normalizedDomain, _excludes))
            return FilterAction.Exclude;

        if (IsMatch(normalizedDomain, _includes))
            return FilterAction.Include;

        return _nextFilter?.Process(domain) ?? FilterAction.Default;
    }

    // Whether the name or a parent of it is listed. Each is asked for by itself: a sorted list read at the name's
    // place alone misses a parent whenever another entry sorts between the two, as example-cdn.com does between
    // example.com and www.example.com
    private static bool IsMatch(string domain, HashSet<string> domains)
    {
        if (domains.Count == 0)
            return false;

        var lookup = domains.GetAlternateLookup<ReadOnlySpan<char>>();
        var name = domain.AsSpan();
        while (!lookup.Contains(name)) {
            var dotIndex = name.IndexOf('.');
            if (dotIndex < 0)
                return false;

            name = name[(dotIndex + 1)..];
        }

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>It passes the call to the next stage: its own lists are no outside configuration.</remarks>
    public Task Reconfigure(CancellationToken cancellationToken) =>
        _nextFilter?.Reconfigure(cancellationToken) ?? Task.CompletedTask;

    /// <summary>Stops listening to the next stage, and disposes it when this stage was made to.</summary>
    public void Dispose()
    {
        // or a next stage that lives on would hold this one
        if (_nextFilter != null)
            _nextFilter.Changed -= NextFilter_Changed;

        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}
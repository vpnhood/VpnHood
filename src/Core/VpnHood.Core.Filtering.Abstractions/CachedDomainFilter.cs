using VpnHood.Net.Toolkit.Collections;

namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Provides a stage of a domain filter pipe that remembers its next stage's verdict for each domain name.
/// </summary>
/// <remarks>
/// A verdict expires once it has gone unused for the timeout. When the next stage raises <see cref="Changed"/>, every
/// remembered verdict is dropped, and the event is raised as this stage's own.
/// </remarks>
public class CachedDomainFilter : IDomainFilter
{
    private readonly IDomainFilter? _nextFilter;
    private readonly bool _autoDisposeNextFilter;
    private readonly TimeoutDictionary<string, TimeoutItem<FilterAction>> _cache;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedDomainFilter"/> class.
    /// </summary>
    /// <param name="nextFilter">
    /// The stage whose verdicts to remember, or <see langword="null"/>, which makes every verdict
    /// <see cref="FilterAction.Default"/>.
    /// </param>
    /// <param name="timeout">How long a verdict may go unused before it expires.</param>
    /// <param name="autoDisposeNextFilter">
    /// <see langword="true"/> to dispose <paramref name="nextFilter"/> with this stage.
    /// </param>
    public CachedDomainFilter(IDomainFilter? nextFilter, TimeSpan timeout, bool autoDisposeNextFilter = true)
    {
        _nextFilter = nextFilter;
        _autoDisposeNextFilter = autoDisposeNextFilter;
        _cache = new TimeoutDictionary<string, TimeoutItem<FilterAction>>(timeout);

        // self-invalidation: when the inner chain announces a change (a live gate swap somewhere below),
        // drop every memoized verdict so none outlives the chain that produced it, and roll the event up
        if (nextFilter != null)
            nextFilter.Changed += (_, _) => {
                ClearCache();
                Changed?.Invoke(this, EventArgs.Empty);
            };
    }

    /// <summary>Decides what happens to a flow for a domain name: the next stage's verdict, remembered.</summary>
    /// <param name="domain">The name, or <see langword="null"/>, whose verdict is not remembered.</param>
    /// <returns>The verdict; <see cref="FilterAction.Default"/> when the pipe has no objection.</returns>
    /// <remarks>Names are remembered as given, so names that differ only in case are remembered apart.</remarks>
    public FilterAction Process(string? domain)
    {
        if (domain == null)
            return _nextFilter?.Process(domain) ?? FilterAction.Default;

        if (_cache.TryGetValue(domain, out var cachedAction))
            return cachedAction.Value;

        var action = _nextFilter?.Process(domain) ?? FilterAction.Default;
        _cache.TryAdd(domain, new TimeoutItem<FilterAction>(action));
        return action;
    }

    /// <inheritdoc/>
    /// <remarks>It passes the call to the next stage.</remarks>
    public Task Reconfigure(CancellationToken cancellationToken) =>
        _nextFilter?.Reconfigure(cancellationToken) ?? Task.CompletedTask;

    /// <inheritdoc/>
    /// <value>The next stage's value, or <see langword="true"/> without one: this stage holds no rule.</value>
    public bool IsEmpty => _nextFilter?.IsEmpty ?? true;

    /// <summary>Drops every remembered verdict, expired or not.</summary>
    public void ClearCache()
    {
        _cache.RemoveAll();
    }

    /// <summary>Releases the remembered verdicts, and disposes the next stage when this stage was made to.</summary>
    public void Dispose()
    {
        _cache.Dispose();
        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}

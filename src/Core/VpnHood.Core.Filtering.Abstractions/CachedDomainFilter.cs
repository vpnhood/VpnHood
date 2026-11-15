using VpnHood.Net.Toolkit.Collections;

namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Provides a stage of a domain filter pipe that remembers its next stage's verdict for each domain name.
/// </summary>
/// <remarks>
/// A verdict expires once it has gone unused for the timeout. When the next stage raises <see cref="Changed"/>, every
/// remembered verdict is dropped, and the event is raised as this stage's own; a verdict being looked up at that
/// moment answers its own lookup, and is remembered for no later one.
/// </remarks>
public class CachedDomainFilter : IDomainFilter
{
    private readonly IDomainFilter? _nextFilter;
    private readonly bool _autoDisposeNextFilter;
    private readonly TimeSpan _timeout;

    // Replaced, not emptied, when the rules change: a lookup under way then puts its verdict, which may be the old
    // rules', into the cache it began with, which no later lookup reads. Emptied instead, the cache gave that
    // verdict to a lookup that began after the change. The lock is for the replacing and the disposal, so that no
    // cache is made after the disposal.
    private volatile TimeoutDictionary<string, TimeoutItem<FilterAction>> _cache;
    private readonly Lock _cacheLock = new();
    private bool _isDisposed;

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
        _timeout = timeout;
        _cache = new TimeoutDictionary<string, TimeoutItem<FilterAction>>(timeout);
        if (nextFilter != null)
            nextFilter.Changed += NextFilter_Changed;
    }

    // self-invalidation: when the inner chain announces a change (a live gate swap somewhere below),
    // drop every memoized verdict so none outlives the chain that produced it, and roll the event up
    private void NextFilter_Changed(object? sender, EventArgs e)
    {
        ClearCache();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Decides what happens to a flow for a domain name: the next stage's verdict, remembered.</summary>
    /// <param name="domain">The name, or <see langword="null"/>, whose verdict is not remembered.</param>
    /// <returns>The verdict; <see cref="FilterAction.Default"/> when the pipe has no objection.</returns>
    /// <remarks>Names are remembered as given, so names that differ only in case are remembered apart.</remarks>
    public FilterAction Process(string? domain)
    {
        if (domain == null)
            return _nextFilter?.Process(domain) ?? FilterAction.Default;

        // one cache for the whole lookup, the one it began with (see the field)
        var cache = _cache;
        if (cache.TryGetValue(domain, out var cachedAction))
            return cachedAction.Value;

        var action = _nextFilter?.Process(domain) ?? FilterAction.Default;
        cache.TryAdd(domain, new TimeoutItem<FilterAction>(action));
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
    /// <remarks>A verdict being looked up at that moment is remembered for no later lookup.</remarks>
    public void ClearCache()
    {
        TimeoutDictionary<string, TimeoutItem<FilterAction>> oldCache;
        lock (_cacheLock) {
            if (_isDisposed)
                return;

            oldCache = _cache;
            _cache = new TimeoutDictionary<string, TimeoutItem<FilterAction>>(_timeout);
        }

        oldCache.Dispose();
    }

    /// <summary>
    /// Releases the remembered verdicts, stops listening to the next stage, and disposes it when this stage was made
    /// to.
    /// </summary>
    public void Dispose()
    {
        // or a next stage that lives on would hold this one, and clear its released cache at every change
        if (_nextFilter != null)
            _nextFilter.Changed -= NextFilter_Changed;

        lock (_cacheLock) {
            _isDisposed = true;
            _cache.Dispose();
        }

        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}

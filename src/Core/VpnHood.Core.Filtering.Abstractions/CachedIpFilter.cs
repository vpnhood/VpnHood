using VpnHood.Net.Toolkit.Collections;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Provides a stage of an IP filter pipe that remembers its next stage's verdict for each protocol and destination.
/// </summary>
/// <remarks>
/// A verdict is remembered by the flow's protocol with its destination, as a stage below may decide by the protocol.
/// It expires once it has gone unused for the timeout. When the next stage raises <see cref="Changed"/>, every
/// remembered verdict is dropped, and the event is raised as this stage's own; a verdict being looked up at that
/// moment answers its own lookup, and is remembered for no later one.
/// </remarks>
public class CachedIpFilter : IIpFilter
{
    private readonly IIpFilter _nextFilter;
    private readonly bool _autoDisposeNextFilter;
    private readonly TimeSpan _timeout;

    // Replaced, not emptied, when the rules change: a lookup under way then puts its verdict, which may be the old
    // rules', into the cache it began with, which no later lookup reads. Emptied instead, the cache gave that
    // verdict to a lookup that began after the change. The lock is for the replacing and the disposal, so that no
    // cache is made after the disposal.
    private volatile TimeoutDictionary<(IpProtocol Protocol, IpEndPointValue EndPoint), TimeoutItem<FilterAction>> _cache;
    private readonly Lock _cacheLock = new();
    private bool _isDisposed;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedIpFilter"/> class.
    /// </summary>
    /// <param name="nextFilter">The stage whose verdicts to remember.</param>
    /// <param name="timeout">How long a verdict may go unused before it expires.</param>
    /// <param name="autoDisposeNextFilter">
    /// <see langword="true"/> to dispose <paramref name="nextFilter"/> with this stage.
    /// </param>
    public CachedIpFilter(IIpFilter nextFilter, TimeSpan timeout, bool autoDisposeNextFilter = true)
    {
        _nextFilter = nextFilter;
        _autoDisposeNextFilter = autoDisposeNextFilter;
        _timeout = timeout;
        _cache = new TimeoutDictionary<(IpProtocol Protocol, IpEndPointValue EndPoint), TimeoutItem<FilterAction>>(timeout);
        nextFilter.Changed += NextFilter_Changed;
    }

    // self-invalidation: when the inner chain announces a change (a live gate swap somewhere below),
    // drop every memoized verdict so none outlives the chain that produced it, and roll the event up
    private void NextFilter_Changed(object? sender, EventArgs e)
    {
        ClearCache();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>The verdict remembered for the protocol and the destination answers.</remarks>
    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        // one cache for the whole lookup, the one it began with (see the field)
        var cache = _cache;
        var key = (protocol, endPoint);
        if (cache.TryGetValue(key, out var cachedAction))
            return cachedAction.Value;

        var action = _nextFilter.Process(protocol, endPoint);
        cache.TryAdd(key, new TimeoutItem<FilterAction>(action));
        return action;
    }

    /// <inheritdoc/>
    /// <remarks>It passes the call to the next stage.</remarks>
    public Task Reconfigure(CancellationToken cancellationToken) => _nextFilter.Reconfigure(cancellationToken);

    /// <inheritdoc/>
    /// <value>The next stage's value: this stage holds no rule.</value>
    public bool IsEmpty => _nextFilter.IsEmpty;

    /// <summary>Drops every remembered verdict, expired or not.</summary>
    /// <remarks>A verdict being looked up at that moment is remembered for no later lookup.</remarks>
    public void ClearCache()
    {
        TimeoutDictionary<(IpProtocol Protocol, IpEndPointValue EndPoint), TimeoutItem<FilterAction>> oldCache;
        lock (_cacheLock) {
            if (_isDisposed)
                return;

            oldCache = _cache;
            _cache = new TimeoutDictionary<(IpProtocol Protocol, IpEndPointValue EndPoint), TimeoutItem<FilterAction>>(_timeout);
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
        _nextFilter.Changed -= NextFilter_Changed;
        lock (_cacheLock) {
            _isDisposed = true;
            _cache.Dispose();
        }

        if (_autoDisposeNextFilter)
            _nextFilter.Dispose();
    }
}

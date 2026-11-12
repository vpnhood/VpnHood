using VpnHood.Net.Toolkit.Collections;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Provides a stage of an IP filter pipe that remembers its next stage's verdict for each destination.
/// </summary>
/// <remarks>
/// A verdict is remembered by the destination alone, whatever the flow's protocol, so the next stage's verdicts must
/// not depend on the protocol. A verdict expires once it has gone unused for the timeout. When the next stage raises
/// <see cref="Changed"/>, every remembered verdict is dropped, and the event is raised as this stage's own.
/// </remarks>
public class CachedIpFilter : IIpFilter
{
    private readonly IIpFilter _nextFilter;
    private readonly bool _autoDisposeNextFilter;
    private readonly TimeoutDictionary<IpEndPointValue, TimeoutItem<FilterAction>> _cache;

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
        _cache = new TimeoutDictionary<IpEndPointValue, TimeoutItem<FilterAction>>(timeout);

        // self-invalidation: when the inner chain announces a change (a live gate swap somewhere below),
        // drop every memoized verdict so none outlives the chain that produced it, and roll the event up
        nextFilter.Changed += (_, _) => {
            ClearCache();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The verdict remembered for the destination answers, whatever protocol asked for it first.
    /// </remarks>
    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        if (_cache.TryGetValue(endPoint, out var cachedAction))
            return cachedAction.Value;

        var action = _nextFilter.Process(protocol, endPoint);
        _cache.TryAdd(endPoint, new TimeoutItem<FilterAction>(action));
        return action;
    }

    /// <inheritdoc/>
    /// <remarks>It passes the call to the next stage.</remarks>
    public Task Reconfigure(CancellationToken cancellationToken) => _nextFilter.Reconfigure(cancellationToken);

    /// <inheritdoc/>
    /// <value>The next stage's value: this stage holds no rule.</value>
    public bool IsEmpty => _nextFilter.IsEmpty;

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
            _nextFilter.Dispose();
    }
}

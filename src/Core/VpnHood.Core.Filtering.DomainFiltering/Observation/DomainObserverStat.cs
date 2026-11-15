using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.DomainFiltering.Observation;

/// <summary>
/// Holds counts of the domain filter's verdicts on the flows whose name it found: what the filter said, before any
/// later rule of the client changed the flow's way.
/// </summary>
/// <remarks>
/// Each count is kept atomically, as flows are counted on several threads at once; the total and the others are
/// separate counts, so a read between the two steps of one flow sees the total one ahead.
/// </remarks>
public class DomainObserverStat
{
    private int _totalCount;
    private int _blockedCount;
    private int _includeCount;
    private int _excludeCount;

    /// <summary>Gets the number of flows counted.</summary>
    public int TotalCount => Volatile.Read(ref _totalCount);

    /// <summary>Gets the number of flows the domain filter blocked.</summary>
    public int BlockedCount => Volatile.Read(ref _blockedCount);

    /// <summary>
    /// Gets the number of flows the domain filter included or had no objection to, which an IP filter may still have
    /// excluded or blocked.
    /// </summary>
    public int IncludeCount => Volatile.Read(ref _includeCount);

    /// <summary>Gets the number of flows the domain filter excluded.</summary>
    public int ExcludeCount => Volatile.Read(ref _excludeCount);

    internal void Update(FilterAction action)
    {
        Interlocked.Increment(ref _totalCount);
        switch (action) {
            case FilterAction.Default: // counted as included unless ip filter drops it (Rarely happens)
            case FilterAction.Include:
                Interlocked.Increment(ref _includeCount);
                break;
            case FilterAction.Exclude:
                Interlocked.Increment(ref _excludeCount);
                break;
            case FilterAction.Block:
                Interlocked.Increment(ref _blockedCount);
                break;
        }
    }

}
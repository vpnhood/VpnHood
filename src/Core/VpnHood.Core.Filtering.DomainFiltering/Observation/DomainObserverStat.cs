using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.DomainFiltering.Observation;

/// <summary>
/// Holds counts of the domain filter's verdicts on the flows whose name it found: what the filter said, before any
/// later rule of the client changed the flow's way.
/// </summary>
/// <remarks>The counts are not kept atomically, so flows counted at the same moment may go uncounted.</remarks>
public class DomainObserverStat
{
    /// <summary>Gets the number of flows counted.</summary>
    public int TotalCount { get; internal set; }

    /// <summary>Gets the number of flows the domain filter blocked.</summary>
    public int BlockedCount { get; internal set; }

    /// <summary>
    /// Gets the number of flows the domain filter included or had no objection to, which an IP filter may still have
    /// excluded or blocked.
    /// </summary>
    public int IncludeCount { get; internal set; }

    /// <summary>Gets the number of flows the domain filter excluded.</summary>
    public int ExcludeCount { get; internal set; }

    internal void Update(FilterAction action)
    {
        TotalCount++;
        switch (action) {
            case FilterAction.Default: // counted as included unless ip filter drops it (Rarely happens)
            case FilterAction.Include:
                IncludeCount++;
                break;
            case FilterAction.Exclude:
                ExcludeCount++;
                break;
            case FilterAction.Block:
                BlockedCount++;
                break;
        }
    }

}
using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.DomainFiltering.Observation;

public class DomainObserverStat
{
    public int TotalCount { get; internal set; }
    public int BlockedCount { get; internal set; }
    public int IncludeCount { get; internal set; }
    public int ExcludeCount { get; internal set; }

    public void Update(FilterAction action)
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
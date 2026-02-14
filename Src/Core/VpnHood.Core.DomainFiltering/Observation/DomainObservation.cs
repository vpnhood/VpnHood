using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.DomainFiltering.Observation;

public class DomainObservation
{
    public required string DomainName { get; init; }
    public FilterAction Action { get; set; }
    public DomainObservationProtocol Protocol { get; set; }
    public DateTime LastObservedTime { get; set; } = DateTime.Now;
    public int Count { get; set; } = 1;
}
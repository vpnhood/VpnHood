using VpnHood.Core.SniFiltering;

namespace VpnHood.Core.SniFiltering.Observation;

public class DomainObservation
{
    public required string DomainName { get; init; }
    public DomainFilterAction Action { get; set; }
    public domainObservationProtocol Protocol { get; set; }
    public DateTime LastObservedTime { get; set; } = DateTime.Now;
    public int Count { get; set; } = 1;
}
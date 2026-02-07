namespace VpnHood.Core.DomainFiltering;

public record DomainObservation(string DomainName, DomainFilterAction Action, DateTime LastObservedTime);
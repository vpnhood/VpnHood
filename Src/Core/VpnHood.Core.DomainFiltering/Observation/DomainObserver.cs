namespace VpnHood.Core.DomainFiltering.Observation;

public class DomainObserver
{
    private readonly Dictionary<string, DomainObservation> _observations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lockObject = new();

    public IReadOnlyList<DomainObservation> Observations {
        get {
            lock (_lockObject) {
                return _observations.Values.ToList();
            }
        }
    }

    public void Track(string domainName, DomainFilterAction action, DomainObservationProtocol protocol, DateTime observedTime)
    {
        if (string.IsNullOrEmpty(domainName))
            return;

        lock (_lockObject) {
            if (_observations.TryGetValue(domainName, out var existing)) {
                existing.Count++;
                existing.LastObservedTime = observedTime;
                existing.Action = action;
                existing.Protocol = protocol;
            }
            else {
                _observations[domainName] = new DomainObservation {
                    DomainName = domainName,
                    Action = action,
                    Protocol = protocol,
                    LastObservedTime = observedTime,
                };
            }
        }
    }

    public void Clear()
    {
        lock (_lockObject) {
            _observations.Clear();
        }
    }
}

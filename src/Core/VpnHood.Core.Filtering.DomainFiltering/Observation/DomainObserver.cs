using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Filtering.DomainFiltering.Observation;

public class DomainObserver(EventId sniEventId)
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

    public void Track(string? domainName, FilterAction action, 
        DomainObservationProtocol protocol, IpEndPointValue? destinationEndPoint = null)
    {
        if (string.IsNullOrEmpty(domainName))
            return;

        // log new domain observation
        VhLogger.Instance.LogDebug(sniEventId, "Domain: {Domain}, DestEp: {DestEp}, Protocol: {protocol}",
            VhLogger.FormatHostName(domainName), VhLogger.Format(destinationEndPoint), protocol);

        // update or add observation
        lock (_lockObject) {
            if (_observations.TryGetValue(domainName, out var existing)) {
                existing.Count++;
                existing.LastObservedTime = FastDateTime.Now;
                existing.Action = action;
                existing.Protocol = protocol;
            }
            else {
                _observations[domainName] = new DomainObservation {
                    DomainName = domainName,
                    Action = action,
                    Protocol = protocol,
                    LastObservedTime = FastDateTime.Now,
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

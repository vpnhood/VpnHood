using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Filtering.DomainFiltering.Observation;

/// <summary>
/// Keeps a record of the domain names the client's domain filter has seen, one entry a name.
/// </summary>
/// <param name="sniEventId">The event ID under which each name recorded is logged.</param>
/// <remarks>
/// Names are compared ignoring case. Its methods are safe to call from several threads, and update the entries under
/// the observer's own lock; the entries <see cref="Observations"/> returns are the record's own, so a caller's reads
/// of them are not synchronized with those updates.
/// </remarks>
public class DomainObserver(EventId sniEventId)
{
    private readonly Dictionary<string, DomainObservation> _observations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lockObject = new();

    /// <summary>
    /// Gets a copy of the list of the names recorded. The entries in it are the record's own, which later sightings
    /// update.
    /// </summary>
    public IReadOnlyList<DomainObservation> Observations {
        get {
            lock (_lockObject) {
                return [.. _observations.Values];
            }
        }
    }

    /// <summary>
    /// Records a sighting of a domain name: a new entry, or the name's entry counted once more, with the verdict and
    /// protocol of this sighting.
    /// </summary>
    /// <param name="domainName">The name, or <see langword="null"/> or empty, which records nothing.</param>
    /// <param name="action">The verdict the name got.</param>
    /// <param name="protocol">The protocol of the flow in which the name was seen.</param>
    /// <param name="destinationEndPoint">The flow's destination, which is only logged.</param>
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
                existing.LastObservedTime = FastDateTime.UtcNow;
                existing.Action = action;
                existing.Protocol = protocol;
            }
            else {
                _observations[domainName] = new DomainObservation {
                    DomainName = domainName,
                    Action = action,
                    Protocol = protocol,
                    LastObservedTime = FastDateTime.UtcNow,
                };
            }
        }
    }

    /// <summary>Removes every name from the record.</summary>
    public void Clear()
    {
        lock (_lockObject) {
            _observations.Clear();
        }
    }
}

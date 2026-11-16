using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.DomainFiltering.Observation;

/// <summary>
/// Represents a domain name the client's domain filter has seen, with its last verdict.
/// </summary>
/// <remarks>
/// <see cref="DomainObserver"/> updates the same instance each time the name is seen again.
/// </remarks>
public class DomainObservation
{
    /// <summary>Gets the domain name, as first seen.</summary>
    public required string DomainName { get; init; }

    /// <summary>Gets or sets the verdict the name got when last seen.</summary>
    public FilterAction Action { get; set; }

    /// <summary>Gets or sets the protocol of the flow in which the name was last seen.</summary>
    public DomainObservationProtocol Protocol { get; set; }

    /// <summary>Gets or sets when the name was last seen, in UTC.</summary>
    public DateTime LastObservedTime { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets how many times the name has been seen. The default is 1.</summary>
    public int Count { get; set; } = 1;
}
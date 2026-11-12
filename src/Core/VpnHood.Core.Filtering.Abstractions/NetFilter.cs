using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Holds the filters a VPN client or server applies to its flows: an IP filter, a domain filter and an IP mapper,
/// each optional.
/// </summary>
public class NetFilter : IDisposable
{
    /// <summary>Gets the IP mapper, or <see langword="null"/> for none.</summary>
    public IIpMapper? IpMapper { get; init; }

    /// <summary>Gets the IP filter pipe, or <see langword="null"/> for none.</summary>
    public IIpFilter? IpFilter { get; init; }

    /// <summary>Gets the domain filter pipe, or <see langword="null"/> for none.</summary>
    public IDomainFilter? DomainFilter { get; init; }

    /// <summary>
    /// Tells both filter pipes that their configuration outside the program may have changed: the IP pipe, then the
    /// domain pipe (<see cref="IIpFilter.Reconfigure"/>).
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Reconfigure(CancellationToken cancellationToken)
    {
        if (IpFilter != null)
            await IpFilter.Reconfigure(cancellationToken).Vhc();

        if (DomainFilter != null)
            await DomainFilter.Reconfigure(cancellationToken).Vhc();
    }

    /// <summary>Disposes the IP mapper and both filter pipes.</summary>
    public void Dispose()
    {
        IpMapper?.Dispose();
        IpFilter?.Dispose();
        DomainFilter?.Dispose();
    }
}
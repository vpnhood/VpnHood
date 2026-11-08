using System.Net;

namespace VpnHood.Net.IpLocations;

/// <summary>
/// Represents a provider that finds where an IP address is.
/// </summary>
public interface IIpLocationProvider : ICurrentIpLocationProvider
{
    /// <summary>Finds where an IP address is.</summary>
    /// <param name="ipAddress">The IP address.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the location.</returns>
    Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken);
}
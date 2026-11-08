namespace VpnHood.Net.IpLocations;

/// <summary>
/// Represents a provider that finds where this device's public IP address is.
/// </summary>
/// <remarks>
/// A provider that does not support a lookup throws <see cref="NotSupportedException"/>, which the composite providers
/// pass over without logging.
/// </remarks>
public interface ICurrentIpLocationProvider : IDisposable
{
    /// <summary>Finds where this device's public IP address is.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the location.</returns>
    /// <exception cref="NotSupportedException">The provider does not support the lookup.</exception>
    Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken);
}
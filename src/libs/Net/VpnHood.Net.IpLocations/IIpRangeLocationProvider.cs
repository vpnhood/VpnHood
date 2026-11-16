using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.IpLocations;

/// <summary>
/// Represents a provider that also gives each country's IP ranges, from a local database.
/// </summary>
public interface IIpRangeLocationProvider : IIpLocationProvider
{
    /// <summary>Gets the countries the database has IP ranges for.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the countries' two-letter codes, in upper case.</returns>
    Task<string[]> GetCountryCodes(CancellationToken cancellationToken);

    /// <summary>Gets a country's IP ranges.</summary>
    /// <param name="countryCode">The country's two-letter code.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the country's ranges, in order.</returns>
    Task<IpRangeOrderedList> GetIpRanges(string countryCode, CancellationToken cancellationToken);
}
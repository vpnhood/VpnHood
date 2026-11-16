using VpnHood.Net.IpLocations.Providers.Onlines;

namespace VpnHood.Net.IpLocations;

/// <summary>
/// Provides the default IP location provider, and a location's path.
/// </summary>
public static class IpLocationProviderFactory
{
    /// <summary>Creates the default provider, the online <see cref="IpApiCoLocationProvider"/>.</summary>
    /// <param name="httpClient">The HTTP client the requests go through; the caller keeps owning it.</param>
    /// <param name="userAgent">The User-Agent header the requests send.</param>
    /// <returns>A new provider.</returns>
    public static IIpLocationProvider CreateDefault(HttpClient httpClient, string userAgent)
        => new IpApiCoLocationProvider(httpClient, userAgent);

    /// <summary>
    /// Builds a location's path: the country code, then the region's name, or the city's when the region is unknown.
    /// </summary>
    /// <param name="countryCode">The country's two-letter code.</param>
    /// <param name="regionName">The region's name, or <see langword="null"/>.</param>
    /// <param name="cityName">The city's name, or <see langword="null"/>.</param>
    /// <returns>The path, such as <c>US/California</c>, or the country code alone when both names are unknown.</returns>
    public static string GetPath(string countryCode, string? regionName, string? cityName)
    {
        return $"{countryCode}/{regionName ?? cityName ?? ""}".Trim('/');
    }
}
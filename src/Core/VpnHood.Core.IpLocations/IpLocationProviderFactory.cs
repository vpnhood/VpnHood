using VpnHood.Core.IpLocations.Providers.Onlines;

namespace VpnHood.Core.IpLocations;

public static class IpLocationProviderFactory
{
    public static IIpLocationProvider CreateDefault(HttpClient httpClient, string userAgent)
        => new IpApiCoLocationProvider(httpClient, userAgent);

    public static string GetPath(string countryCode, string? regionName, string? cityName)
    {
        return $"{countryCode}/{regionName ?? cityName ?? ""}".Trim('/');
    }
}
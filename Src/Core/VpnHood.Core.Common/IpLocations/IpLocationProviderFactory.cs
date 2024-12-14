using VpnHood.Core.Common.IpLocations.Providers;

namespace VpnHood.Core.Common.IpLocations;

public class IpLocationProviderFactory
{
    public IIpLocationProvider CreateDefault(HttpClient httpClient, string userAgent) 
        => new IpApiCoLocationProvider(httpClient, userAgent);

    public static string GetPath(string countryCode, string? regionName, string? cityName)
    {
        return $"{countryCode}/{regionName ?? cityName ?? ""}".Trim('/');
    }
}
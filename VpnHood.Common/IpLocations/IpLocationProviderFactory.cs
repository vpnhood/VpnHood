using VpnHood.Common.IpLocations.Providers;

namespace VpnHood.Common.IpLocations;

public class IpLocationProviderFactory
{
    public IIpLocationProvider CreateDefault() => new IpApiCoLocationProvider();

    public static string GetPath(string countryCode, string? regionName, string? cityName)
    {
        return $"{countryCode}/{regionName ?? cityName ?? ""}".Trim('/');
    }
}
using System.Globalization;
using System.Net;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.IpLocations.Providers.Onlines;

/// <summary>
/// Provides the current location, the country only, from Cloudflare's trace page (cloudflare.com/cdn-cgi/trace).
/// </summary>
/// <param name="httpClient">The HTTP client the requests go through; the caller keeps owning it.</param>
/// <param name="userAgent">The User-Agent header the requests send.</param>
public class CloudflareLocationProvider(HttpClient httpClient, string userAgent) : ICurrentIpLocationProvider
{
    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        const string url = "https://cloudflare.com/cdn-cgi/trace";

        // get data from the service provider
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        requestMessage.Headers.Add("User-Agent", userAgent);
        var responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken).Vhc();
        responseMessage.EnsureSuccessStatusCode();
        var content = await responseMessage.Content.ReadAsStringAsync(cancellationToken).Vhc();

        // Split the response into lines
        var lines = content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var ip = lines.Single(x => x.StartsWith("ip=", StringComparison.OrdinalIgnoreCase)).Split('=')[1];
        var countryCode = lines.Single(x => x.StartsWith("loc=", StringComparison.OrdinalIgnoreCase)).Split('=')[1];

        var ipLocation = new IpLocation {
            IpAddress = IPAddress.Parse(ip),
            CountryName = new RegionInfo(countryCode).EnglishName,
            CountryCode = countryCode,
            RegionName = null,
            CityName = null
        };

        return ipLocation;
    }

    /// <summary>Does nothing: the provider holds nothing to release.</summary>
    public void Dispose()
    {
    }
}
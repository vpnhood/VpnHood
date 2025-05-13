using System.Globalization;
using System.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Common.IpLocations.Providers.Onlines;

public class CloudflareLocationProvider(HttpClient httpClient, string userAgent) : IIpLocationProvider
{
    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        const string url = "https://cloudflare.com/cdn-cgi/trace";

        // get data from the service provider
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        requestMessage.Headers.Add("User-Agent", userAgent);
        var responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken).VhConfigureAwait();
        responseMessage.EnsureSuccessStatusCode();
        var content = await responseMessage.Content.ReadAsStringAsync().VhConfigureAwait();

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
}
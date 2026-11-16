using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.IpLocations.Providers.Onlines;

/// <summary>
/// Provides locations from ipapi.co: the country, and the region and city where known.
/// </summary>
/// <param name="httpClient">The HTTP client the requests go through; the caller keeps owning it.</param>
/// <param name="userAgent">The User-Agent header the requests send.</param>
public class IpApiCoLocationProvider(HttpClient httpClient, string userAgent) : IIpLocationProvider
{
    internal class ApiLocation
    {
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(IPAddressConverter))]
        public required IPAddress Ip { get; set; }

        [JsonPropertyName("country_code")]
        public required string CountryCode { get; set; }

        [JsonPropertyName("region")]
        public string? RegionName { get; set; }

        [JsonPropertyName("city")]
        public string? CityName { get; set; }
    }

    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://ipapi.co/{ipAddress}/json/");
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var uri = new Uri("https://ipapi.co/json/");
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

    private static async Task<IpLocation> GetLocation(HttpClient httpClient, Uri url, string userAgent,
        CancellationToken cancellationToken)
    {
        // get json from the service provider
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        requestMessage.Headers.Add("User-Agent", userAgent);
        var responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken).Vhc();
        responseMessage.EnsureSuccessStatusCode();
        var json = await responseMessage.Content.ReadAsStringAsync(cancellationToken).Vhc();

        var apiLocation = JsonUtils.Deserialize<ApiLocation>(json);
        var ipLocation = new IpLocation {
            IpAddress = apiLocation.Ip,
            CountryName = new RegionInfo(apiLocation.CountryCode).EnglishName,
            CountryCode = apiLocation.CountryCode,
            RegionName = NormalizePart(apiLocation.RegionName),
            CityName = NormalizePart(apiLocation.CityName)
        };

        return ipLocation;
    }

    // a part of the location the service does not know: it leaves it out, sends it empty, or sends "NA"
    private static string? NormalizePart(string? value)
    {
        return string.IsNullOrEmpty(value) || value.Equals("NA", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    /// <summary>Does nothing: the provider holds nothing to release.</summary>
    public void Dispose()
    {
    }
}
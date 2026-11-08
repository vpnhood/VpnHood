using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.IpLocations.Providers.Onlines;

/// <summary>
/// Provides locations from ip2location.io: the country, and the region and city where known.
/// </summary>
/// <param name="httpClient">The HTTP client the requests go through; the caller keeps owning it.</param>
/// <param name="userAgent">The User-Agent header the requests send.</param>
/// <param name="apiKey">The API key, or <see langword="null"/>; the current location's lookup sends none.</param>
public class IpLocationIoProvider(HttpClient httpClient, string userAgent, string? apiKey)
    : IIpLocationProvider
{
    internal class ApiLocation
    {
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(IPAddressConverter))]
        public required IPAddress Ip { get; set; }

        [JsonPropertyName("country_code")]
        public required string CountryCode { get; set; }

        [JsonPropertyName("region_name")]
        public string? RegionName { get; set; }

        [JsonPropertyName("city_name")]
        public string? CityName { get; set; }

        [JsonPropertyName("latitude")]
        public double? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; set; }
    }

    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://api.ip2location.io/?key={apiKey}&ip={ipAddress}");
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var uri = new Uri("https://api.ip2location.io"); // no need for key
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

    private static async Task<IpLocation> GetLocation(HttpClient httpClient, Uri url, string userAgent,
        CancellationToken cancellationToken)
    {
        // get data from the service provider
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        requestMessage.Headers.Add("User-Agent", userAgent);
        var responseMessage = await httpClient
            .SendAsync(requestMessage, cancellationToken)
            .Vhc();

        responseMessage.EnsureSuccessStatusCode();
        var json = await responseMessage.Content
            .ReadAsStringAsync(cancellationToken)
            .Vhc();

        var apiLocation = JsonUtils.Deserialize<ApiLocation>(json);
        var regionName = apiLocation.RegionName?.ToUpper() == "NA" || string.IsNullOrEmpty(apiLocation.CityName)
            ? null
            : apiLocation.RegionName;

        var cityCode = apiLocation.CityName?.ToUpper() == "NA" || string.IsNullOrEmpty(apiLocation.CityName)
            ? null
            : apiLocation.CityName;

        var ipLocation = new IpLocation {
            IpAddress = apiLocation.Ip,
            CountryName = new RegionInfo(apiLocation.CountryCode).EnglishName,
            CountryCode = apiLocation.CountryCode,
            RegionName = regionName,
            CityName = cityCode
        };

        return ipLocation;
    }

    /// <summary>Does nothing: the provider holds nothing to release.</summary>
    public void Dispose()
    {
    }
}
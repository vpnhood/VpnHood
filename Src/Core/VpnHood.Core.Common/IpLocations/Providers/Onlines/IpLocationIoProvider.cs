using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Common.IpLocations.Providers;

public class IpLocationIoProvider(HttpClient httpClient, string userAgent, string? apiKey)
    : IIpLocationProvider
{
    internal class ApiLocation
    {
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(IPAddressConverter))]
        public required IPAddress Ip { get; set; }

        [JsonPropertyName("country_code")] public required string CountryCode { get; set; }

        [JsonPropertyName("region_name")] public string? RegionName { get; set; }

        [JsonPropertyName("city_name")] public string? CityName { get; set; }

        [JsonPropertyName("latitude")] public string? Latitude { get; set; }

        [JsonPropertyName("longitude")] public string? Longitude { get; set; }
    }

    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://api.ip2location.io/?key={apiKey}&ip={ipAddress}");
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

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
            .VhConfigureAwait();

        responseMessage.EnsureSuccessStatusCode();
        var json = await responseMessage.Content
            .ReadAsStringAsync()
            .VhWait(cancellationToken)
            .VhConfigureAwait();

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
}
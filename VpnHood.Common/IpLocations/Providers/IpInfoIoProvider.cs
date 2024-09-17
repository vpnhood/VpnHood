using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Utils;

namespace VpnHood.Common.IpLocations.Providers;

public class IpInfoIoProvider(HttpClient httpClient, string userAgent, string apiKey) 
    : IIpLocationProvider
{
    internal class ApiLocation
    {
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(IPAddressConverter))]
        public required IPAddress Ip { get; set; }
        
        [JsonPropertyName("country")
        ] public required string CountryCode { get; set; }

        [JsonPropertyName("region")] 
        public string? RegionName { get; set; }

        [JsonPropertyName("city")] 
        public string? CityName { get; set; }

        [JsonPropertyName("loc")]
        public string? GeoLoc { get; set; }


    }

    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://ipinfo.io/{ipAddress}?token={apiKey}");
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

    public Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://ipinfo.io?token={apiKey}");
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

    private static async Task<IpLocation> GetLocation(HttpClient httpClient, Uri url, string userAgent,
        CancellationToken cancellationToken)
    {
        // get json from the service provider
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        requestMessage.Headers.Add("User-Agent", userAgent);
        var responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken).VhConfigureAwait();
        responseMessage.EnsureSuccessStatusCode();
        var json = await responseMessage.Content.ReadAsStringAsync().VhConfigureAwait();
        var apiLocation = VhUtil.JsonDeserialize<ApiLocation>(json);

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
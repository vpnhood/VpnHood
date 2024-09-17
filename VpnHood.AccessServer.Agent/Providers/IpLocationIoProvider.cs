using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.IpLocations;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Agent.Providers;

public class IpLocationIoProvider(string userAgent, string apiKey) : IIpLocationProvider
{
    internal class ApiLocation
    {
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(IPAddressConverter))]
        public required IPAddress Ip { get; set; }

        [JsonPropertyName("country_name")] public required string CountryName { get; set; }

        [JsonPropertyName("country_code")] public required string CountryCode { get; set; }

        [JsonPropertyName("region_name")] public string? RegionName { get; set; }


        [JsonPropertyName("city_name")] public string? CityName { get; set; }

    }

    public Task<IpLocation> GetLocation(HttpClient httpClient, IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://api.ip2location.io/?key={apiKey}&ip={ipAddress}");
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

    public Task<IpLocation> GetLocation(HttpClient httpClient, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    private static async Task<IpLocation> GetLocation(HttpClient httpClient, Uri url, string userAgent,
        CancellationToken cancellationToken)
    {
        // get json from the service provider
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        requestMessage.Headers.Add("User-Agent", userAgent);
        var responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken).VhConfigureAwait();
        responseMessage.EnsureSuccessStatusCode();
        var json = await responseMessage.Content.ReadAsStringAsync(cancellationToken).VhConfigureAwait();
        var apiLocation = VhUtil.JsonDeserialize<ApiLocation>(json);

        var regionName = apiLocation.RegionName?.ToUpper() == "NA" || string.IsNullOrEmpty(apiLocation.CityName)
            ? null
            : apiLocation.RegionName;

        var cityCode = apiLocation.CityName?.ToUpper() == "NA" || string.IsNullOrEmpty(apiLocation.CityName)
            ? null
            : apiLocation.CityName;

        var ipLocation = new IpLocation {
            IpAddress = apiLocation.Ip,
            CountryName = apiLocation.CountryName,
            CountryCode = apiLocation.CountryCode,
            RegionName = regionName,
            RegionCode = regionName,
            CityName = cityCode,
            CityCode = cityCode,
            ContinentCode = null
        };

        return ipLocation;
    }
}
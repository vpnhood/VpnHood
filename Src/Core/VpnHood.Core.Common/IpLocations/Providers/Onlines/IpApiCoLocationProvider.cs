using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Common.IpLocations.Providers.Onlines;

public class IpApiCoLocationProvider(HttpClient httpClient, string userAgent) : IIpLocationProvider
{
    internal class ApiLocation
    {
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(IPAddressConverter))]
        public required IPAddress Ip { get; set; }

        [JsonPropertyName("country_code")] public required string CountryCode { get; set; }

        [JsonPropertyName("region")] public string? RegionName { get; set; }

        [JsonPropertyName("city")] public string? CityName { get; set; }
    }

    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://ipapi.co/{ipAddress}/json/");
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

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
            RegionName = apiLocation.RegionName == "NA" || string.IsNullOrEmpty(apiLocation.RegionName)
                ? null
                : apiLocation.RegionName,
            CityName = apiLocation.CityName == "NA" || string.IsNullOrEmpty(apiLocation.RegionName)
                ? null
                : apiLocation.CityName
        };

        return ipLocation;
    }
}
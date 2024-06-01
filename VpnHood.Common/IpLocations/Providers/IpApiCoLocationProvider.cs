using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Utils;

namespace VpnHood.Common.IpLocations.Providers;

public class IpApiCoLocationProvider(string userAgent) : IIpLocationProvider
{
    internal class ApiLocation
    {
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(IPAddressConverter))]
        public required IPAddress Ip { get; set; }

        [JsonPropertyName("country_name")]
        public required string CountryName { get; set; }
       
        [JsonPropertyName("country_code")]
        public required string CountryCode { get; set; }

        [JsonPropertyName("region")]
        public string? RegionName { get; set; }
       
        [JsonPropertyName("region_code")]
        public string? RegionCode { get; set; }
        
        [JsonPropertyName("city")]
        public string? CityName { get; set; }
        
        [JsonPropertyName("continent_code")]
        public string? ContinentCode { get; set; }
    }

    public Task<IpLocation> GetLocation(HttpClient httpClient, IPAddress ipAddress)
    {
        var uri = new Uri($"https://ipapi.co/{ipAddress}/json/");
        return GetLocation(httpClient, uri, userAgent);
    }

    public Task<IpLocation> GetLocation(HttpClient httpClient)
    {
        var uri = new Uri("https://ipapi.co/json/");
        return GetLocation(httpClient, uri, userAgent);
    }

    private static async Task<IpLocation> GetLocation(HttpClient httpClient, Uri url, string userAgent)
    {
        // get json from the service provider
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        requestMessage.Headers.Add("User-Agent", userAgent);
        var responseMessage = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);
        responseMessage.EnsureSuccessStatusCode();
        var json = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        var apiLocation = VhUtil.JsonDeserialize<ApiLocation>(json);
        var ipLocation = new IpLocation
        {
            IpAddress = apiLocation.Ip,
            CountryName = apiLocation.CountryName,
            CountryCode = apiLocation.CountryCode,
            RegionName = apiLocation.RegionName == "NA" ? null : apiLocation.RegionName,
            RegionCode = apiLocation.RegionCode == "NA" ? null : apiLocation.RegionCode,
            CityName = apiLocation.CityName == "NA" ? null : apiLocation.CityName,
            CityCode = apiLocation.CityName == "NA" ? null : apiLocation.CityName,
            ContinentCode = apiLocation.ContinentCode == "NA" ? null : apiLocation.ContinentCode
        };

        return ipLocation;
    }
}
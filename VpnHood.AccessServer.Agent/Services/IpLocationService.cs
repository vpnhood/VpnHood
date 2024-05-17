using System.Net;
using System.Text.Json.Serialization;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Agent.IpLocations;

namespace VpnHood.AccessServer.Agent.Services;

public class IpLocationService(IHttpClientFactory httpClientFactory)
    : IIpLocationService
{
    public class ApiLocation
    {
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

    public async Task<IpLocation> GetLocation(IPAddress ipAddress)
{
        var client = httpClientFactory.CreateClient();

        // get json from the service provider
        var uri = new Uri($"https://ipapi.co/{ipAddress}/json/");
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);
        requestMessage.Headers.Add("User-Agent", "VpnHood-AccessManager");
        var responseMessage = await client.SendAsync(requestMessage);
        responseMessage.EnsureSuccessStatusCode();
        var json = await responseMessage.Content.ReadAsStringAsync();

        var apiLocation = GmUtil.JsonDeserialize<ApiLocation>(json);
        var ipLocation = new IpLocation
        {
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
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.IpLocations.Providers.Onlines;

/// <summary>
/// Provides locations from internetdata.io: the country, and with an API key the region and city where known.
/// </summary>
/// <param name="httpClient">The HTTP client the requests go through; the caller keeps owning it.</param>
/// <param name="userAgent">The User-Agent header the requests send.</param>
/// <param name="apiKey">The API key, or <see langword="null"/>; the current location's lookup sends none.</param>
public class InternetDataProvider(HttpClient httpClient, string userAgent, string? apiKey)
    : IIpLocationProvider
{
    internal class ApiLocation
    {
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(IPAddressConverter))]
        public required IPAddress Ip { get; set; }

        [JsonPropertyName("location")]
        public required ApiLocationPart Location { get; set; }
    }

    internal class ApiLocationPart
    {
        [JsonPropertyName("country")]
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
        var uri = new Uri($"https://api.internetdata.io/{ipAddress}");
        return GetLocation(httpClient, uri, userAgent, apiKey, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var uri = new Uri("https://api.internetdata.io/myip");
        return GetLocation(httpClient, uri, userAgent, apiKey: null, cancellationToken);
    }

    private static async Task<IpLocation> GetLocation(HttpClient httpClient, Uri url, string userAgent,
        string? apiKey, CancellationToken cancellationToken)
    {
        // get data from the service provider; the key goes in a header, never in the URL
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        requestMessage.Headers.Add("User-Agent", userAgent);
        if (!string.IsNullOrEmpty(apiKey))
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var responseMessage = await httpClient
            .SendAsync(requestMessage, cancellationToken)
            .Vhc();

        responseMessage.EnsureSuccessStatusCode();
        var json = await responseMessage.Content
            .ReadAsStringAsync(cancellationToken)
            .Vhc();

        var apiLocation = JsonUtils.Deserialize<ApiLocation>(json);
        var ipLocation = new IpLocation {
            IpAddress = apiLocation.Ip,
            CountryName = new RegionInfo(apiLocation.Location.CountryCode).EnglishName,
            CountryCode = apiLocation.Location.CountryCode,
            RegionName = NormalizePart(apiLocation.Location.RegionName),
            CityName = NormalizePart(apiLocation.Location.CityName)
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

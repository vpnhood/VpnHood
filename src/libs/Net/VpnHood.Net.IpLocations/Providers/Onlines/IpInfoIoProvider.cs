using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.IpLocations.Providers.Onlines;

/// <summary>
/// Provides locations from ipinfo.io: the country, and the region, without diacritics, and city where known.
/// </summary>
/// <param name="httpClient">The HTTP client the requests go through; the caller keeps owning it.</param>
/// <param name="userAgent">The User-Agent header the requests send.</param>
/// <param name="apiKey">The API key, or <see langword="null"/>; the current location's lookup sends none.</param>
public class IpInfoIoProvider(HttpClient httpClient, string userAgent, string? apiKey)
    : IIpLocationProvider
{
    internal class ApiLocation
    {
        [JsonPropertyName("ip")]
        [JsonConverter(typeof(IPAddressConverter))]
        public required IPAddress Ip { get; set; }

        [JsonPropertyName("country")
        ]
        public required string CountryCode { get; set; }

        [JsonPropertyName("region")]
        public string? RegionName { get; set; }

        [JsonPropertyName("city")]
        public string? CityName { get; set; }

        [JsonPropertyName("loc")]
        public string? GeoLoc { get; set; }
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        foreach (var chr in normalizedString) {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(chr);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                stringBuilder.Append(chr);
        }

        return stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }

    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://ipinfo.io/{ipAddress}?token={apiKey}");
        return GetLocation(httpClient, uri, userAgent, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The request failed.</exception>
    public Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var uri = new Uri("https://ipinfo.io");
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

        var regionName = NormalizePart(apiLocation.RegionName);
        var ipLocation = new IpLocation {
            IpAddress = apiLocation.Ip,
            CountryName = new RegionInfo(apiLocation.CountryCode).EnglishName,
            CountryCode = apiLocation.CountryCode,
            RegionName = regionName != null ? RemoveDiacritics(regionName) : null,
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
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Common.Utils;

public static class IpLocationUtil
{
    public static async Task<string?> GetCountryCode()
    {
        try
        {
            return await IpApi_GetCountryCode();

        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not get country code from IpApi service.");
            return null;
        }
    }

    private static async Task<string?> IpApi_GetCountryCode()
    {
        // get json from the service provider
        var httpClient = new HttpClient();
        var uri = new Uri("https://ipapi.co//json/");
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);
        requestMessage.Headers.Add("User-Agent", "VpnHood-Client");
        var responseMessage = await httpClient.SendAsync(requestMessage);
        responseMessage.EnsureSuccessStatusCode();
        var json = await responseMessage.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("country_code", out var countryCodeElement)
            ? countryCodeElement.GetString() : null;
    }
}
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// Transport for the Portal REST API: JSON in and out, RFC 9457 problem+json
/// for failures. The contract is backend-agnostic (no WHMCS concept on the
/// wire) and is published by the portal itself at /openapi.json; today
/// it is served by the vpnhoodiap module.
/// </summary>
public class PortalApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<T> Get<T>(string path, CancellationToken cancellationToken)
    {
        return Send<T>(HttpMethod.Get, path, null, cancellationToken);
    }

    public Task<T> Post<T>(string path, object body, CancellationToken cancellationToken)
    {
        return Send<T>(HttpMethod.Post, path, body, cancellationToken);
    }

    public async Task Delete(string path, CancellationToken cancellationToken)
    {
        using var response = await SendCore(HttpMethod.Delete, path, null, cancellationToken).Vhc();
        await EnsureSuccess(response, cancellationToken).Vhc();
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCore(method, path, body, cancellationToken).Vhc();
        await EnsureSuccess(response, cancellationToken).Vhc();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).Vhc();
        T? value;
        try {
            value = JsonSerializer.Deserialize<T>(responseText, JsonOptions);
        }
        catch (JsonException ex) {
            throw new PortalApiException($"The portal answered {path} with unreadable JSON. {ex.Message}",
                (int)response.StatusCode);
        }

        return value ?? throw new PortalApiException($"The portal answered {path} with an empty body.",
            (int)response.StatusCode);
    }

    private Task<HttpResponseMessage> SendCore(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        if (body != null)
            request.Content = JsonContent.Create(body, options: JsonOptions);
        return httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// The base address is the endpoint itself (…/api.php) and resources hang off
    /// it, so a relative Uri would replace its last segment instead of extending it.
    /// </summary>
    private Uri BuildUri(string path)
    {
        var baseAddress = httpClient.BaseAddress
            ?? throw new InvalidOperationException("The portal base address has not been set.");
        return new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + path);
    }

    /// <summary>Turns a problem+json failure into an exception carrying its machine code.</summary>
    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var statusCode = (int)response.StatusCode;
        var problem = await TryReadProblem(response, cancellationToken).Vhc();
        if (problem == null)
            throw new PortalApiException(
                $"The portal answered {statusCode} {response.ReasonPhrase}.", statusCode);

        // a 404 with no problem body is a web server saying "no such URL"; a 404 WITH
        // one is the portal saying it is not active on that install — both surface here
        throw new PortalApiException(problem.Detail ?? problem.Title ?? $"Portal error {statusCode}.",
            statusCode, problem.Code);
    }

    private static async Task<PortalProblem?> TryReadProblem(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).Vhc();
            return string.IsNullOrWhiteSpace(responseText)
                ? null
                : JsonSerializer.Deserialize<PortalProblem>(responseText, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException) {
            return null; // not our error shape (proxy page, HTML 404, …)
        }
    }

    private class PortalProblem
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public string? Code { get; init; }
    }
}

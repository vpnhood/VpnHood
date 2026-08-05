using System.Text;
using System.Text.Json;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// Hand-written client for the Portal API action envelope:
/// POST { "action": "...", ...params } → { "success": true, "data": {...} }
/// or { "success": false, "error": "..." }. The contract is backend-agnostic
/// (no WHMCS concept on the wire); today it is served by the vpnhoodiap module.
/// </summary>
public class PortalApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> Invoke<T>(string action, IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { ["action"] = action };
        if (parameters != null)
            foreach (var parameter in parameters)
                body[parameter.Key] = parameter.Value;

        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync((Uri?)null, content, cancellationToken).Vhc();
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).Vhc();

        Envelope<T>? envelope;
        try {
            envelope = JsonSerializer.Deserialize<Envelope<T>>(responseText, JsonOptions);
        }
        catch (JsonException) {
            envelope = null;
        }
        if (envelope == null)
            throw new PortalApiException(
                $"The portal did not answer its envelope (HTTP {(int)response.StatusCode}).", (int)response.StatusCode);
        if (!envelope.Success || envelope.Data == null)
            throw new PortalApiException(envelope.Error ?? "Unknown portal error.", (int)response.StatusCode);

        return envelope.Data;
    }

    private class Envelope<T>
    {
        public bool Success { get; init; }
        public T? Data { get; init; }
        public string? Error { get; init; }
    }
}

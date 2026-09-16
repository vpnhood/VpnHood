using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.WebServer.Client;

// What the six HTTP clients share: the request, the JSON and the failure. Paths are relative to the
// client's base address, which is where the page was served from, so the cookie that carries the
// pairing goes with every call by itself. The JSON is camelCase, as the server writes and reads it,
// and every type comes from the source-generated context, so a trimmed build - the browser - needs
// no reflection for it. A failure the server reported as an ApiError comes back as the exception
// it names, with the same Data, which is what the UI reads whether the call crossed HTTP or not.
internal abstract class AppApiClientBase(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = AppApiJsonContext.Default
    };

    protected Task<TResult> GetAsync<TResult>(string path, IReadOnlyDictionary<string, object?>? query, CancellationToken cancellationToken)
    {
        return ReadAsync<TResult>(HttpMethod.Get, path, query, null, cancellationToken);
    }

    // For a reply the server may leave empty (204): null then, never a failure.
    protected async Task<TResult?> GetOrDefaultAsync<TResult>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken).Vhc();
        return response.StatusCode == HttpStatusCode.NoContent
            ? default
            : await ReadBodyAsync<TResult>(response, cancellationToken).Vhc();
    }

    protected async Task<string> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken).Vhc();
        return await response.Content.ReadAsStringAsync(cancellationToken).Vhc();
    }

    protected async Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken).Vhc();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).Vhc();
    }

    protected Task PostAsync(string path, IReadOnlyDictionary<string, object?>? query, CancellationToken cancellationToken)
    {
        return NoResultAsync(HttpMethod.Post, path, query, null, cancellationToken);
    }

    protected Task PostBodyAsync<TBody>(string path, TBody body, CancellationToken cancellationToken)
    {
        return NoResultAsync(HttpMethod.Post, path, null, JsonBody(body), cancellationToken);
    }

    protected Task<TResult> PostAsync<TResult>(string path, IReadOnlyDictionary<string, object?>? query, CancellationToken cancellationToken)
    {
        return ReadAsync<TResult>(HttpMethod.Post, path, query, null, cancellationToken);
    }

    protected Task<TResult> PostAsync<TBody, TResult>(string path, IReadOnlyDictionary<string, object?>? query, TBody body, CancellationToken cancellationToken)
    {
        return ReadAsync<TResult>(HttpMethod.Post, path, query, JsonBody(body), cancellationToken);
    }

    protected Task PutBodyAsync<TBody>(string path, TBody body, CancellationToken cancellationToken)
    {
        return NoResultAsync(HttpMethod.Put, path, null, JsonBody(body), cancellationToken);
    }

    protected Task<TResult> PutAsync<TResult>(string path, IReadOnlyDictionary<string, object?>? query, CancellationToken cancellationToken)
    {
        return ReadAsync<TResult>(HttpMethod.Put, path, query, null, cancellationToken);
    }

    protected Task<TResult> PutAsync<TBody, TResult>(string path, TBody body, CancellationToken cancellationToken)
    {
        return ReadAsync<TResult>(HttpMethod.Put, path, null, JsonBody(body), cancellationToken);
    }

    protected Task<TResult> PatchAsync<TBody, TResult>(string path, TBody body, CancellationToken cancellationToken)
    {
        return ReadAsync<TResult>(HttpMethod.Patch, path, null, JsonBody(body), cancellationToken);
    }

    protected Task DeleteAsync(string path, IReadOnlyDictionary<string, object?>? query, CancellationToken cancellationToken)
    {
        return NoResultAsync(HttpMethod.Delete, path, query, null, cancellationToken);
    }

    private static JsonContent JsonBody<TBody>(TBody body)
    {
        return JsonContent.Create(body, TypeInfo<TBody>());
    }

    private static JsonTypeInfo<T> TypeInfo<T>()
    {
        return (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));
    }

    private async Task NoResultAsync(HttpMethod method, string path, IReadOnlyDictionary<string, object?>? query,
        HttpContent? content, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, query, content, cancellationToken).Vhc();
    }

    private async Task<TResult> ReadAsync<TResult>(HttpMethod method, string path, IReadOnlyDictionary<string, object?>? query,
        HttpContent? content, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, query, content, cancellationToken).Vhc();
        return await ReadBodyAsync<TResult>(response, cancellationToken).Vhc();
    }

    private static async Task<TResult> ReadBodyAsync<TResult>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).Vhc();
        var result = await JsonSerializer.DeserializeAsync(stream, TypeInfo<TResult>(), cancellationToken).Vhc();
        return result ?? throw new ApiException("The reply was empty where a value was expected.",
            (int)response.StatusCode, null, null, null);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, IReadOnlyDictionary<string, object?>? query,
        HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUrl(path, query));
        request.Content = content;
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).Vhc();
        if (response.IsSuccessStatusCode)
            return response;

        var text = await response.Content.ReadAsStringAsync(cancellationToken).Vhc();
        var statusCode = (int)response.StatusCode;
        response.Dispose();
        throw ToException(text, statusCode);
    }

    // The server's ApiError as the exception it names - a NotExistsException for a missing profile,
    // an InvalidOperationException for a bad key - with its Data, as the in-process call would have
    // thrown it. Anything else (the pairing hint page, a proxy's own answer) is an ApiException
    // with the status and the text.
    private static Exception ToException(string text, int statusCode)
    {
        if (!string.IsNullOrWhiteSpace(text)) {
            ApiError? apiError = null;
            try {
                apiError = JsonSerializer.Deserialize(text, AppApiJsonContext.Default.ApiError);
            }
            catch (JsonException) {
                // not an ApiError body
            }

            if (apiError?.TypeName != null)
                return apiError.ToException();
        }

        return new ApiException($"The app's API answered {statusCode}.", statusCode, text, null, null);
    }

    private static string BuildUrl(string path, IReadOnlyDictionary<string, object?>? query)
    {
        if (query == null)
            return path;

        var builder = new StringBuilder(path);
        var separator = '?';
        foreach (var (name, value) in query) {
            if (value == null)
                continue;
            builder.Append(separator).Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(QueryValue(value)));
            separator = '&';
        }

        return builder.ToString();
    }

    // As the server's route parameters read them: lower-case booleans, enum names, invariant numbers.
    private static string QueryValue(object value)
    {
        return value switch {
            bool boolean => boolean ? "true" : "false",
            Enum enumeration => enumeration.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    protected static string Segment(string value)
    {
        return Uri.EscapeDataString(value);
    }
}

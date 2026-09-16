using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.Core.Toolkit.Extensions;


// ReSharper disable UnusedMember.Global
namespace VpnHood.Core.Toolkit.ApiClients;

public class ApiClientBase : ApiClientCommon
{
    private class HttpNoResult;

    protected readonly struct HttpResult<T>
    {
        public required HttpResponseMessage ResponseMessage { get; init; }
        public required T Object { get; init; }
        public required string Text { get; init; }
    }

    protected JsonSerializerOptions JsonSerializerSettings => field ??= CreateSerializerSettings();
    protected HttpClient? HttpClient;
    public ILogger Logger { get; set; } = NullLogger.Instance;
    public EventId LoggerEventId { get; set; } = new();

    public ApiClientBase(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    protected ApiClientBase()
    {
    }

    protected virtual JsonSerializerOptions CreateSerializerSettings()
    {
        var settings = new JsonSerializerOptions();
        return settings;
    }

    public bool ReadResponseAsString { get; set; }

    protected virtual async Task<HttpResult<T?>> ReadObjectResponseAsync<T>(HttpResponseMessage response,
        IReadOnlyDictionary<string, IEnumerable<string>> headers, CancellationToken cancellationToken)
    {
        // a reply that is not JSON is read as it stands - a log as text, an image as bytes - because
        // there is nothing to deserialize; the content type is what says so, since the same C# type
        // can come back either way (a JSON string is quoted, a text/plain one is not)
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) {
            if (typeof(T) == typeof(string)) {
                var plainText = await response.Content.ReadAsStringAsync(cancellationToken).Vhc();
                return new HttpResult<T?> { ResponseMessage = response, Object = (T)(object)plainText, Text = plainText };
            }

            if (typeof(T) == typeof(byte[])) {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).Vhc();
                return new HttpResult<T?> { ResponseMessage = response, Object = (T)(object)bytes, Text = string.Empty };
            }
        }

        if (ReadResponseAsString) {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).Vhc();
            try {
                var typedBody = JsonSerializer.Deserialize<T>(responseText, JsonSerializerSettings);
                return new HttpResult<T?> { ResponseMessage = response, Object = typedBody, Text = responseText };
            }
            catch (JsonException exception) {
                var message = "Could not deserialize the response body string as " + typeof(T).FullName + ".";
                throw new ApiException(message, (int)response.StatusCode, responseText, headers, exception);
            }
        }

        try {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).Vhc();
            var typedBody = await JsonSerializer
                .DeserializeAsync<T>(responseStream, JsonSerializerSettings, cancellationToken).Vhc();
            return new HttpResult<T?> { ResponseMessage = response, Object = typedBody, Text = string.Empty };
        }
        catch (JsonException exception) {
            var message = "Could not deserialize the response body stream as " + typeof(T).FullName + ".";
            throw new ApiException(message, (int)response.StatusCode, string.Empty, headers, exception);
        }
    }

    protected string ConvertToString(object? value, CultureInfo cultureInfo)
    {
        if (value == null) {
            return "";
        }

        if (value is Enum) {
            var name = Enum.GetName(value.GetType(), value);
            if (name != null) {
                var field = value.GetType().GetTypeInfo().GetDeclaredField(name);
                if (field != null) {
                    if (field.GetCustomAttribute(typeof(EnumMemberAttribute)) is EnumMemberAttribute attribute) {
                        return attribute.Value ?? name;
                    }
                }

                var converted =
                    Convert.ToString(Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), cultureInfo));

                return converted ?? "";
            }
        }
        else if (value is bool b) {
            return Convert.ToString(b, cultureInfo).ToLowerInvariant();
        }
        else if (value is byte[] bytes) {
            return Convert.ToBase64String(bytes);
        }
        else if (value.GetType().IsArray) {
            var array = ((Array)value).OfType<object>();
            return string.Join(",", array.Select(o => ConvertToString(o, cultureInfo)));
        }

        var result = Convert.ToString(value, cultureInfo);
        return result ?? "";
    }

    protected async Task<string> HttpSendAsync(HttpMethod httpMethod, string urlPart,
        Dictionary<string, object?>? parameters = null, object? data = null,
        CancellationToken cancellationToken = default)
    {
        var res = await HttpSendExAsync<HttpNoResult>(httpMethod, urlPart, parameters, data, cancellationToken).Vhc();
        return res.Text;
    }

    protected async Task<T> HttpSendAsync<T>(HttpMethod httpMethod, string urlPart,
        Dictionary<string, object?>? parameters = null, object? data = null,
        CancellationToken cancellationToken = default)
    {
        var res = await HttpSendExAsync<T>(httpMethod, urlPart, parameters, data, cancellationToken).Vhc();
        return res.Object;
    }

    protected async Task<HttpResult<T>> HttpSendExAsync<T>(HttpMethod httpMethod, string urlPart,
        Dictionary<string, object?>? parameters = null, object? data = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage();
        request.Method = httpMethod;
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

        // only the methods that carry a body get one; a DELETE used to be sent with a
        // literal "null" payload, which some servers and proxies reject outright
        if (httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put || httpMethod == HttpMethod.Patch) {
            var content = new StringContent(JsonSerializer.Serialize(data, JsonSerializerSettings));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            request.Content = content;
        }

        // don't return Task as request will be disposed
        return await HttpSendAsync<T>(urlPart, parameters, request, cancellationToken).Vhc();
    }

    protected async Task<string> HttpSendAsync(string urlPart, Dictionary<string, object?>? parameters,
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var res = await HttpSendAsync<HttpNoResult>(urlPart, parameters, request, cancellationToken).Vhc();
        return res.Text;
    }

    protected virtual async Task<HttpResult<T>> HttpSendAsync<T>(string urlPart,
        Dictionary<string, object?>? parameters,
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try {
            var ret = await HttpSendAsyncImpl<T>(urlPart, parameters, request, cancellationToken).Vhc();

            // report the log
            Logger.LogInformation(LoggerEventId,
                "API Called. Method: {Method}, Uri: {RequestUri} => StatusCode: {StatusCode}.",
                request.Method, request.RequestUri, ret.ResponseMessage.StatusCode);

            return ret;
        }
        catch (ApiException ex) {
            Logger.LogError(LoggerEventId, ex,
                "API Called. Method: {Method}, Uri: {RequestUri} => StatusCode: {StatusCode}.", request.Method,
                request.RequestUri, ex.StatusCode);
            throw;
        }
        catch (Exception ex) {
            Logger.LogError(LoggerEventId, ex, "API Called. Method: {Method}, Uri: {RequestUri}, Failed.",
                request.Method, request.RequestUri);
            throw;
        }
    }

    private async Task<HttpResult<T>> HttpSendAsyncImpl<T>(string urlPart, Dictionary<string, object?>? parameters,
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        parameters ??= new Dictionary<string, object?>();

        var urlBuilder = new StringBuilder();
        urlBuilder.Append(urlPart);
        if (parameters.Any()) {
            urlBuilder.Append("?");
            foreach (var parameter in parameters.Where(x => x.Value != null)) {
                urlBuilder
                    .Append(Uri.EscapeDataString(parameter.Key) + "=")
                    .Append(Uri.EscapeDataString(ConvertToString(parameter.Value, CultureInfo.InvariantCulture)))
                    .Append('&');
            }

            urlBuilder.Length--;
        }

        var client = HttpClient ?? throw new Exception("HttpClient has not been set.");
        await PrepareRequestAsync(client, request, urlBuilder, cancellationToken).Vhc();


        using var response =
            await HttpClientSendAsync(client, request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .Vhc();
        var headers = response.Headers.ToDictionary(h => h.Key, h => h.Value);

        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        if (response.Content?.Headers != null) {
            foreach (var item in response.Content.Headers)
                headers[item.Key] = item.Value;
        }

        await ProcessResponseAsync(client, response, cancellationToken).Vhc();

        var status = (int)response.StatusCode;
        if (status is >= 200 and < 300) {
            // 204 says there is no body; a typed caller gets default for it rather than a parse failure
            if (typeof(T) == typeof(HttpNoResult) || status == (int)HttpStatusCode.NoContent)
                return new HttpResult<T> { ResponseMessage = response, Object = default!, Text = string.Empty };

            var objectResponse =
                await ReadObjectResponseAsync<T>(response, headers, cancellationToken).Vhc();
            if (objectResponse.Object == null)
                throw new ApiException("Response was null which was not expected.", status, objectResponse.Text,
                    headers, null);

            return objectResponse!;
        }

        var responseData = response.Content != null
            ? await response.Content.ReadAsStringAsync(cancellationToken).Vhc()
            : null;

        // RFC 9457 problem+json is the REST standard for errors, announced by its
        // media type — how a non-.NET backend reports failures. Refit it as an
        // ApiError so the same ApiException reaches callers whichever dialect the
        // server speaks: Message from the problem's detail, the machine code in
        // Data["Code"] and as ExceptionTypeName. ApiError bodies keep flowing
        // through the ApiException constructor's own parsing, as before.
        var problemError = TryConvertProblemDetails(response, responseData);
        if (problemError != null)
            throw ToException(problemError) ??
                  new ApiException(problemError.Message, status, problemError.ToJson(), headers, null);

        // an ApiError body names the exception the server threw; whether this client can rebuild
        // that type is the client's own answer (ToException), since the toolkit cannot see its
        // types. Read with this client's own settings - a camelCase server writes "typeName"
        if (!string.IsNullOrWhiteSpace(responseData)) {
            ApiError? apiError = null;
            try {
                apiError = JsonSerializer.Deserialize<ApiError>(responseData, JsonSerializerSettings);
            }
            catch (JsonException) {
                // not an ApiError body
            }

            var named = apiError?.TypeName != null ? ToException(apiError) : null;
            if (named != null)
                throw named;
        }

        throw new ApiException("The HTTP status code of the response was not expected (" + status + ").", status,
            responseData, headers, null);
    }

    /// <summary>An RFC 9457 problem+json response as an ApiError, or null when the response is not one.</summary>
    private static ApiError? TryConvertProblemDetails(HttpResponseMessage response, string? responseData)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(responseData))
            return null;

        try {
            using var problem = JsonDocument.Parse(responseData);
            if (problem.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var code = GetStringProperty(problem.RootElement, "code");
            var detail = GetStringProperty(problem.RootElement, "detail")
                         ?? GetStringProperty(problem.RootElement, "title");

            var apiError = new ApiError {
                TypeName = code ?? "HttpProblem",
                Message = detail ?? code ?? "The server reported a problem."
            };
            if (code != null)
                apiError.Data["Code"] = code;

            // RFC 9457 extension members (e.g. a conflict's existingCodeSuffix) ride
            // beside the standard fields; carry the scalar ones so callers can branch
            // on them the same way they branch on Data["Code"].
            foreach (var member in problem.RootElement.EnumerateObject()) {
                if (member.Name is "type" or "title" or "status" or "code" or "detail" or "instance")
                    continue;
                if (member.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                    or JsonValueKind.True or JsonValueKind.False)
                    apiError.Data[member.Name] = member.Value.ToString();
            }
            return apiError;
        }
        catch (JsonException) {
            return null; // the media type lied; fall back to the generic path
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    /// <summary>
    /// The exception an ApiError body names, so a caller catches the same type it would have caught
    /// in process. Null - the default - leaves the failure as a plain ApiException, which carries
    /// the name in <see cref="ApiException.ExceptionTypeName" /> either way. A client whose server
    /// throws types the toolkit cannot see overrides this; <see cref="ApiError.ToException" />
    /// covers the ones it can.
    /// </summary>
    protected virtual Exception? ToException(ApiError apiError)
    {
        _ = apiError;
        return null;
    }

    protected virtual Task<HttpResponseMessage> HttpClientSendAsync(HttpClient client, HttpRequestMessage request,
        HttpCompletionOption responseHeadersRead, CancellationToken cancellationToken)
    {
        return client.SendAsync(request, responseHeadersRead, cancellationToken);
    }

    protected Task<T> HttpGetAsync<T>(string urlPart,
        Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        return HttpSendAsync<T>(HttpMethod.Get, urlPart, parameters, null, cancellationToken);
    }

    protected Task<T> HttpPostAsync<T>(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken = default)
    {
        return HttpSendAsync<T>(HttpMethod.Post, urlPart, parameters, data, cancellationToken);
    }

    protected Task HttpPostAsync(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken = default)
    {
        return HttpSendAsync(HttpMethod.Post, urlPart, parameters, data, cancellationToken);
    }

    protected Task<T> HttpPutAsync<T>(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken = default)
    {
        return HttpSendAsync<T>(HttpMethod.Put, urlPart, parameters, data, cancellationToken);
    }

    protected Task HttpPutAsync(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken = default)
    {
        return HttpSendAsync(HttpMethod.Put, urlPart, parameters, data, cancellationToken);
    }

    protected Task<T> HttpPatchAsync<T>(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken = default)
    {
        return HttpSendAsync<T>(HttpMethod.Patch, urlPart, parameters, data, cancellationToken);
    }

    protected Task HttpPatchAsync(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken = default)
    {
        return HttpSendAsync(HttpMethod.Patch, urlPart, parameters, data, cancellationToken);
    }

    protected Task HttpDeleteAsync(string urlPart,
        Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        return HttpSendAsync(HttpMethod.Delete, urlPart, parameters, null, cancellationToken);
    }
}
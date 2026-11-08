using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.Net.Toolkit.Extensions;


// ReSharper disable UnusedMember.Global
namespace VpnHood.Net.Toolkit.ApiClients;

/// <summary>
/// Provides the base of a hand-written API client: it sends JSON requests through an <see cref="System.Net.Http.HttpClient"/>,
/// reads JSON responses, and turns an error response into an exception.
/// </summary>
/// <remarks>
/// A status outside 2xx throws: for an RFC 9457 problem or an <see cref="ApiError"/> body, the exception
/// <see cref="ToException"/> makes, which is none by default; otherwise, an <see cref="ApiException"/>. Each call sent is
/// logged.
/// </remarks>
public class ApiClientBase : ApiClientCommon
{
    private class HttpNoResult;

    /// <summary>Represents a response read as an object.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    protected readonly struct HttpResult<T>
    {
        /// <summary>
        /// Gets the response, already disposed by the time the send returns: its status and headers can be read, not its
        /// content.
        /// </summary>
        public required HttpResponseMessage ResponseMessage { get; init; }

        /// <summary>Gets the object read from the response.</summary>
        public required T Object { get; init; }

        /// <summary>Gets the response's text, when it was read as text; otherwise, empty.</summary>
        public required string Text { get; init; }
    }

    /// <summary>Gets the JSON settings the requests and responses use, made once by <see cref="CreateSerializerSettings"/>.</summary>
    protected JsonSerializerOptions JsonSerializerSettings => field ??= PrepareSerializerSettings();

    /// <summary>The HTTP client the requests go through, or <see langword="null"/> until a derived class sets it.</summary>
    protected HttpClient? HttpClient;

    /// <summary>Gets or sets the logger of the calls. The default logs nothing.</summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>Gets or sets the event ID the log lines carry.</summary>
    public EventId LoggerEventId { get; set; } = new();

    /// <summary>Initializes a new instance of the <see cref="ApiClientBase"/> class.</summary>
    /// <param name="httpClient">The HTTP client the requests go through; the caller keeps owning it.</param>
    public ApiClientBase(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiClientBase"/> class whose derived class sets
    /// <see cref="HttpClient"/>.
    /// </summary>
    protected ApiClientBase()
    {
    }

    /// <summary>
    /// Creates the JSON settings, once. Without a resolver of its own, the settings read by reflection where it is on.
    /// </summary>
    /// <returns>The settings. The default is .NET's defaults.</returns>
    protected virtual JsonSerializerOptions CreateSerializerSettings()
    {
        var settings = new JsonSerializerOptions();
        return settings;
    }

    // Each read and write goes through the settings' GetTypeInfo, which needs a resolver. A client with no
    // context of its own reads by reflection: where that is off, the browser page, the trimmer drops this line.
    private JsonSerializerOptions PrepareSerializerSettings()
    {
        var settings = CreateSerializerSettings();
        if (settings.TypeInfoResolver == null && JsonSerializer.IsReflectionEnabledByDefault)
            settings.TypeInfoResolver = new DefaultJsonTypeInfoResolver();

        return settings;
    }

    private JsonTypeInfo<T> GetJsonTypeInfo<T>()
    {
        return (JsonTypeInfo<T>)JsonSerializerSettings.GetTypeInfo(typeof(T));
    }

    /// <summary>
    /// Gets or sets a value indicating whether a JSON response is read as text first, so the text shows in an error;
    /// otherwise, it is read as a stream.
    /// </summary>
    public bool ReadResponseAsString { get; set; }

    /// <summary>
    /// Reads a success response as an object: as JSON, or a response that is not JSON as text into a <see cref="string"/>
    /// or as bytes into a <see cref="byte"/> array.
    /// </summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="response">The response.</param>
    /// <param name="headers">The response's headers, for an error.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object with the response.</returns>
    /// <exception cref="ApiException">The JSON cannot be read as the type.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
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
                var typedBody = JsonSerializer.Deserialize(responseText, GetJsonTypeInfo<T>());
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
                .DeserializeAsync(responseStream, GetJsonTypeInfo<T>(), cancellationToken).Vhc();
            return new HttpResult<T?> { ResponseMessage = response, Object = typedBody, Text = string.Empty };
        }
        catch (JsonException exception) {
            var message = "Could not deserialize the response body stream as " + typeof(T).FullName + ".";
            throw new ApiException(message, (int)response.StatusCode, string.Empty, headers, exception);
        }
    }

    /// <summary>
    /// Writes a query parameter's value as text: an enum by its <see cref="System.Runtime.Serialization.EnumMemberAttribute"/>
    /// value where reflection is on, else its number, and a combination of flags as .NET writes it; a <see cref="bool"/>
    /// in lower case; bytes in Base64; an array's items separated by commas.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/> for an empty text.</param>
    /// <param name="cultureInfo">The culture the value is written in.</param>
    /// <returns>The text.</returns>
    protected string ConvertToString(object? value, CultureInfo cultureInfo)
    {
        if (value == null) {
            return "";
        }

        if (value is Enum) {
            var name = Enum.GetName(value.GetType(), value);
            if (name != null) {
                // Read by reflection: where that is off, the browser page, the trimmer drops this, and a value
                // goes as its number.
                if (JsonSerializer.IsReflectionEnabledByDefault) {
                    var field = value.GetType().GetTypeInfo().GetDeclaredField(name);
                    if (field?.GetCustomAttribute(typeof(EnumMemberAttribute)) is EnumMemberAttribute attribute) {
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

    /// <summary>Sends a request; the body of a success is not read.</summary>
    /// <param name="httpMethod">The method.</param>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="data">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is empty.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected async Task<string> HttpSendAsync(HttpMethod httpMethod, string urlPart,
        Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken)
    {
        var res = await HttpSendExAsync<HttpNoResult>(httpMethod, urlPart, parameters, data, cancellationToken).Vhc();
        return res.Text;
    }

    /// <summary>Sends a request and reads its response as an object.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="httpMethod">The method.</param>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="data">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object; the default for status 204.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected async Task<T> HttpSendAsync<T>(HttpMethod httpMethod, string urlPart,
        Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken)
    {
        var res = await HttpSendExAsync<T>(httpMethod, urlPart, parameters, data, cancellationToken).Vhc();
        return res.Object;
    }

    /// <summary>Sends a request and reads its response as an object, with the response.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="httpMethod">The method.</param>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="data">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object with the response.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected async Task<HttpResult<T>> HttpSendExAsync<T>(HttpMethod httpMethod, string urlPart,
        Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage();
        request.Method = httpMethod;
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

        // A body only when there is data: no data is no body, not the JSON null.
        if (data != null) {
            var content = new StringContent(JsonSerializer.Serialize(data, JsonSerializerSettings.GetTypeInfo(data.GetType())));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            request.Content = content;
        }

        // don't return Task as request will be disposed
        return await HttpSendAsync<T>(urlPart, parameters, request, cancellationToken).Vhc();
    }

    /// <summary>Sends a request the caller made; the body of a success is not read.</summary>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="request">The request, which the caller keeps owning.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is empty.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected async Task<string> HttpSendAsync(string urlPart, Dictionary<string, object?>? parameters,
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var res = await HttpSendAsync<HttpNoResult>(urlPart, parameters, request, cancellationToken).Vhc();
        return res.Text;
    }

    /// <summary>Sends a request the caller made, and reads its response as an object, with the response.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="request">The request, which the caller keeps owning.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object with the response.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // the caller's own cancellation is no failure; a timeout, which it did not ask for, logs as one below
            Logger.LogDebug(LoggerEventId, "API Called. Method: {Method}, Uri: {RequestUri}, Cancelled.",
                request.Method, request.RequestUri);
            throw;
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
                apiError = JsonSerializer.Deserialize(responseData, GetJsonTypeInfo<ApiError>());
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
    /// Makes the exception an error response names, so a caller catches the same type it would have caught in process.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/>, the default, leaves the failure as an <see cref="ApiException"/>, which carries the name
    /// in <see cref="ApiException.ExceptionTypeName"/> either way. An override can return
    /// <see cref="ApiError.ToException"/>'s, which covers the common types, or its own.
    /// </remarks>
    /// <param name="apiError">The error.</param>
    /// <returns>The exception, or <see langword="null"/> for an <see cref="ApiException"/>.</returns>
    protected virtual Exception? ToException(ApiError apiError)
    {
        _ = apiError;
        return null;
    }

    /// <summary>Sends a request through the HTTP client; a derived class may send it otherwise.</summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="request">The request.</param>
    /// <param name="responseHeadersRead">When the send completes: once the headers are read, or the whole response.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the response.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected virtual Task<HttpResponseMessage> HttpClientSendAsync(HttpClient client, HttpRequestMessage request,
        HttpCompletionOption responseHeadersRead, CancellationToken cancellationToken)
    {
        return client.SendAsync(request, responseHeadersRead, cancellationToken);
    }

    /// <summary>Sends a GET request and reads its response as an object.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object; the default for status 204.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected Task<T> HttpGetAsync<T>(string urlPart,
        Dictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        return HttpSendAsync<T>(HttpMethod.Get, urlPart, parameters, null, cancellationToken);
    }

    /// <summary>Sends a POST request and reads its response as an object.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="data">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object; the default for status 204.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected Task<T> HttpPostAsync<T>(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken)
    {
        return HttpSendAsync<T>(HttpMethod.Post, urlPart, parameters, data, cancellationToken);
    }

    /// <summary>Sends a POST request.</summary>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="data">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected Task HttpPostAsync(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken)
    {
        return HttpSendAsync(HttpMethod.Post, urlPart, parameters, data, cancellationToken);
    }

    /// <summary>Sends a PUT request and reads its response as an object.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="data">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object; the default for status 204.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected Task<T> HttpPutAsync<T>(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken)
    {
        return HttpSendAsync<T>(HttpMethod.Put, urlPart, parameters, data, cancellationToken);
    }

    /// <summary>Sends a PUT request.</summary>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="data">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected Task HttpPutAsync(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken)
    {
        return HttpSendAsync(HttpMethod.Put, urlPart, parameters, data, cancellationToken);
    }

    /// <summary>Sends a PATCH request and reads its response as an object.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="data">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object; the default for status 204.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected Task<T> HttpPatchAsync<T>(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken)
    {
        return HttpSendAsync<T>(HttpMethod.Patch, urlPart, parameters, data, cancellationToken);
    }

    /// <summary>Sends a PATCH request.</summary>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="data">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected Task HttpPatchAsync(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken)
    {
        return HttpSendAsync(HttpMethod.Patch, urlPart, parameters, data, cancellationToken);
    }

    /// <summary>Sends a DELETE request.</summary>
    /// <param name="urlPart">The URL, relative to <see cref="ApiClientCommon.DefaultBaseAddress"/> or absolute.</param>
    /// <param name="parameters">The query's parameters, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ApiException">The status is not a success, or the response cannot be read.</exception>
    /// <exception cref="Exception">
    /// The exception <see cref="ToException"/> makes for an error response, or the client has no <see cref="HttpClient"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or the HTTP client timed out.</exception>
    protected Task HttpDeleteAsync(string urlPart,
        Dictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        return HttpSendAsync(HttpMethod.Delete, urlPart, parameters, null, cancellationToken);
    }
}
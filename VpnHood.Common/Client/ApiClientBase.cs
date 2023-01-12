using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// ReSharper disable UnusedMember.Global
namespace VpnHood.Common.Client;

public class ApiClientBase
{
    private class HttpNoResult
    {

    }

    protected struct HttpResult<T>
    {
        public HttpResult(HttpResponseMessage responseMessage, T responseObject, string responseText)
        {
            ResponseMessage = responseMessage;
            Object = responseObject;
            Text = responseText;
        }

        public HttpResponseMessage ResponseMessage { get; }
        public T Object { get; }
        public string Text { get; }
    }

    protected JsonSerializerOptions JsonSerializerSettings => Settings.Value;
    protected HttpClient HttpClient;
    protected readonly Lazy<JsonSerializerOptions> Settings;
    public ILogger Logger { get; set; } = NullLogger.Instance;
    public EventId LoggerEventId { get; set; } = new();

    public ApiClientBase(HttpClient httpClient)
    {
        HttpClient = httpClient;
        Settings = new Lazy<JsonSerializerOptions>(CreateSerializerSettings);
    }

    public Uri? DefaultBaseAddress { get; set; }
    public AuthenticationHeaderValue? DefaultAuthorization { get; set; }

    protected virtual Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, string url, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    protected virtual Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response, CancellationToken ct)
    {
        return Task.CompletedTask;
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
        if (ReadResponseAsString)
        {
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            try
            {
                var typedBody = JsonSerializer.Deserialize<T>(responseText, JsonSerializerSettings);
                return new HttpResult<T?>(response, typedBody, responseText);
            }
            catch (JsonException exception)
            {
                var message = "Could not deserialize the response body string as " + typeof(T).FullName + ".";
                throw new ApiException(message, (int)response.StatusCode, responseText, headers, exception);
            }
        }

        try
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var typedBody = await JsonSerializer.DeserializeAsync<T>(responseStream, JsonSerializerSettings, cancellationToken).ConfigureAwait(false);
            return new HttpResult<T?>(response, typedBody, string.Empty);
        }
        catch (JsonException exception)
        {
            var message = "Could not deserialize the response body stream as " + typeof(T).FullName + ".";
            throw new ApiException(message, (int)response.StatusCode, string.Empty, headers, exception);
        }
    }

    protected string ConvertToString(object? value, CultureInfo cultureInfo)
    {
        if (value == null)
        {
            return "";
        }

        if (value is Enum)
        {
            var name = Enum.GetName(value.GetType(), value);
            if (name != null)
            {
                var field = IntrospectionExtensions.GetTypeInfo(value.GetType()).GetDeclaredField(name);
                if (field != null)
                {
                    if (CustomAttributeExtensions.GetCustomAttribute(field, typeof(EnumMemberAttribute)) is EnumMemberAttribute attribute)
                    {
                        return attribute.Value ?? name;
                    }
                }

                var converted = Convert.ToString(Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), cultureInfo));
                return converted;
            }
        }
        else if (value is bool b)
        {
            return Convert.ToString(b, cultureInfo).ToLowerInvariant();
        }
        else if (value is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }
        else if (value.GetType().IsArray)
        {
            var array = ((Array)value).OfType<object>();
            return string.Join(",", array.Select(o => ConvertToString(o, cultureInfo)));
        }

        var result = Convert.ToString(value, cultureInfo);
        return result ?? "";
    }

    protected async Task<string> HttpSendAsync(HttpMethod httpMethod, string urlPart,
        Dictionary<string, object?>? parameters = null, object? data = null, CancellationToken cancellationToken = default)
    {
        var res = await HttpSendExAsync<HttpNoResult>(httpMethod, urlPart, parameters, data, cancellationToken);
        return res.Text;
    }

    protected async Task<T> HttpSendAsync<T>(HttpMethod httpMethod, string urlPart,
        Dictionary<string, object?>? parameters = null, object? data = null, CancellationToken cancellationToken = default)
    {
        var res = await HttpSendExAsync<T>(httpMethod, urlPart, parameters, data, cancellationToken);
        return res.Object;
    }

    protected async Task<HttpResult<T>> HttpSendExAsync<T>(HttpMethod httpMethod, string urlPart,
        Dictionary<string, object?>? parameters = null, object? data = null, CancellationToken cancellationToken = default)
    {

        using var request = new HttpRequestMessage();
        request.Method = httpMethod;
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

        if (httpMethod != HttpMethod.Get)
        {
            var content = new StringContent(JsonSerializer.Serialize(data, JsonSerializerSettings));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            request.Content = content;
        }

        return await HttpSendAsync<T>(urlPart, parameters, request, cancellationToken);
    }

    protected async Task<string> HttpSendAsync(string urlPart, Dictionary<string, object?>? parameters,
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var res = await HttpSendAsync<HttpNoResult>(urlPart, parameters, request, cancellationToken);
        return res.Text;
    }

    protected virtual async Task<HttpResult<T>> HttpSendAsync<T>(string urlPart, Dictionary<string, object?>? parameters,
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var ret = await HttpSendAsyncImpl<T>(urlPart, parameters, request, cancellationToken);

            // report the log
            Logger.LogInformation(LoggerEventId, "API Called. Method: {Method}, Uri: {RequestUri} => StatusCode: {StatusCode}.",
                request.Method, request.RequestUri, ret.ResponseMessage.StatusCode);

            return ret;
        }
        catch (ApiException ex)
        {
            Logger.LogError(LoggerEventId, ex, "API Called. Method: {Method}, Uri: {RequestUri} => StatusCode: {StatusCode}.", request.Method, request.RequestUri, ex.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(LoggerEventId, ex, "API Called. Method: {Method}, Uri: {RequestUri}, Failed.", request.Method, request.RequestUri);
            throw;
        }
    }

    private async Task<HttpResult<T>> HttpSendAsyncImpl<T>(string urlPart, Dictionary<string, object?>? parameters,
    HttpRequestMessage request, CancellationToken cancellationToken)
    {
        parameters ??= new Dictionary<string, object?>();

        var urlBuilder = new StringBuilder();
        urlBuilder.Append(urlPart);
        if (parameters.Any())
        {
            urlBuilder.Append("?");
            foreach (var parameter in parameters.Where(x => x.Value != null))
            {
                urlBuilder
                    .Append(Uri.EscapeDataString(parameter.Key) + "=")
                    .Append(Uri.EscapeDataString(ConvertToString(parameter.Value, CultureInfo.InvariantCulture)))
                    .Append('&');
            }
            urlBuilder.Length--;
        }

        var client = HttpClient;
        var url = urlBuilder.ToString();
        request.RequestUri = new Uri(url, UriKind.RelativeOrAbsolute);
        request.Headers.Authorization ??= DefaultAuthorization;

        // build url
        await PrepareRequestAsync(client, request, url, cancellationToken).ConfigureAwait(false);

        // add DefaultBaseAddress if exists and request uri is relative
        if (DefaultBaseAddress != null && !request.RequestUri.IsAbsoluteUri)
            request.RequestUri = new Uri(DefaultBaseAddress, request.RequestUri);

        using var response = await HttpClientSendAsync(client, request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var headers = response.Headers.ToDictionary(h => h.Key, h => h.Value);

        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        if (response.Content?.Headers != null)
        {
            foreach (var item in response.Content.Headers)
                headers[item.Key] = item.Value;
        }

        await ProcessResponseAsync(client, response, cancellationToken).ConfigureAwait(false);

        var status = (int)response.StatusCode;
        if (status is >= 200 and < 300)
        {
            if (typeof(T) == typeof(HttpNoResult))
                return new HttpResult<T>(response, default!, string.Empty);

            var objectResponse = await ReadObjectResponseAsync<T>(response, headers, cancellationToken).ConfigureAwait(false);
            if (objectResponse.Object == null)
                throw new ApiException("Response was null which was not expected.", status, objectResponse.Text, headers, null);

            return objectResponse!;
        }

        var responseData = response.Content != null ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
        throw new ApiException("The HTTP status code of the response was not expected (" + status + ").", status, responseData, headers, null);
    }

    protected virtual Task<HttpResponseMessage> HttpClientSendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption responseHeadersRead, CancellationToken cancellationToken)
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
        return HttpSendAsync<T>(HttpMethod.Put, urlPart, parameters, data, cancellationToken);
    }

    protected Task HttpPatchAsync(string urlPart, Dictionary<string, object?>? parameters, object? data,
        CancellationToken cancellationToken = default)
    {
        return HttpSendAsync(HttpMethod.Patch, urlPart, parameters, data, cancellationToken);
    }

    protected async Task HttpDeleteAsync(string urlPart,
        Dictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        await HttpSendAsync(HttpMethod.Delete, urlPart, parameters, null, cancellationToken);
    }
}
using System.Text.Json;
using System.Net;
using System.Text;

namespace VpnHood.AppLib.WebServer.Controllers;

public abstract class BaseController
{
    protected HttpListenerContext Context { get; set; } = null!;
    protected CancellationToken CancellationToken { get; set; }

    public void SetContext(HttpListenerContext context, CancellationToken cancellationToken = default)
    {
        Context = context;
        CancellationToken = cancellationToken;
    }

    public async Task<T> GetRequestDataAsync<T>()
    {
        using var reader = new StreamReader(Context.Request.InputStream);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? throw new InvalidOperationException("Invalid JSON data");
    }

    public void SendJsonResponse<T>(T data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var buffer = Encoding.UTF8.GetBytes(json);
        Context.Response.StatusCode = 200;
        Context.Response.ContentType = "application/json";
        Context.Response.ContentLength64 = buffer.Length;
        Context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        Context.Response.OutputStream.Close();
    }

    public void SendNoContent()
    {
        Context.Response.StatusCode = 204;
        Context.Response.Close();
    }

    public string? GetQueryParameter(string key)
    {
        return Context.Request.QueryString[key];
    }

    public T? GetQueryParameter<T>(string key, T? defaultValue = default)
    {
        var value = GetQueryParameter(key);
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        try
        {
            if (typeof(T) == typeof(Guid) || typeof(T) == typeof(Guid?))
                return (T)(object)Guid.Parse(value);
            
            if (typeof(T).IsEnum)
                return (T)Enum.Parse(typeof(T), value, true);

            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    public void SendTextResponse(string text, string contentType = "text/plain")
    {
        var buffer = Encoding.UTF8.GetBytes(text);
        Context.Response.StatusCode = 200;
        Context.Response.ContentType = contentType;
        Context.Response.ContentLength64 = buffer.Length;
        Context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        Context.Response.OutputStream.Close();
    }

    public void SendStreamResponse(Stream stream, string contentType = "text/plain")
    {
        Context.Response.StatusCode = 200;
        Context.Response.ContentType = contentType;
        stream.CopyTo(Context.Response.OutputStream);
        Context.Response.OutputStream.Close();
    }

    public string GetUrlSegment(int index)
    {
        var segments = Context.Request.Url?.Segments;
        return segments != null && segments.Length > index ? segments[index].TrimEnd('/') : "";
    }
}
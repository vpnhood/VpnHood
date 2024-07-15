using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;

// ReSharper disable once CheckNamespace
namespace Ga4.Trackers;

public abstract class TrackerBase : ITracker
{
    private static readonly Lazy<HttpClient> HttpClientLazy = new(() => new HttpClient());
    private static HttpClient HttpClient => HttpClientLazy.Value;
    public required string MeasurementId { get; init; }
    public required string SessionId { get; set; }
    public required string ClientId { get; init; }
    public string? UserId { get; init; }
    public string UserAgent { get; set; } = Environment.OSVersion.ToString().Replace(" ", "");
    public bool IsEnabled { get; set; } = true;
    public bool IsAdminDebugView { get; set; }
    public ILogger? Logger { get; set; }
    public EventId LoggerEventId { get; set; } = new(0, "Ga4Tracker");
    public bool ThrowExceptionOnError { get; set; }
    public Dictionary<string, object> UserProperties { get; set; } = new();

    public abstract Task Track(IEnumerable<TrackEvent> trackEvents);
    public Task Track(TrackEvent trackEvent) => Track([trackEvent]);

    public Task TrackError(string action, Exception ex)
    {
        var trackEvent = new TrackEvent
        {
            EventName = "exception",
            Parameters = new Dictionary<string, object>
            {
                { "page_location", "ex/" + action },
                { "page_title", ex.Message }
            }
        };

        return Track([trackEvent]);
    }

    protected void PrepareHttpHeaders(HttpHeaders httpHeaders)
    {
        httpHeaders.Add("User-Agent", UserAgent);
        //requestMessage.Headers.Add("Sec-Ch-Ua", "\"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"114\", \"Microsoft Edge\";v=\"114\"");
        //httpHeaders.Add("Sec-Ch-Ua-Mobile", "1");
        //httpHeaders.Add("Sec-Ch-Ua-Platform", "\"Android\"");
    }

    protected async Task SendHttpRequest(HttpRequestMessage requestMessage, string name, object? jsonData = null)
    {
        if (!IsEnabled)
            return;

        try
        {
            // log
            Logger?.LogInformation(LoggerEventId,
                "Sending Ga4Track: {name}, Url: {Url},  Headers: {Headers}",
                name, requestMessage.RequestUri, JsonSerializer.Serialize(requestMessage.Headers, new JsonSerializerOptions { WriteIndented = true }));

            if (jsonData != null)
            {
                requestMessage.Content = new StringContent(JsonSerializer.Serialize(jsonData));
                requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                // log
                var data = JsonSerializer.Serialize(jsonData, new JsonSerializerOptions { WriteIndented = true });
                Logger?.LogInformation(LoggerEventId, "Ga4Track Data: {Data}", data);
            }

            var res = await HttpClient.SendAsync(requestMessage).ConfigureAwait(false);

            // log
            var result = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            Logger?.LogInformation(LoggerEventId, "Ga4Track Result: {Result}", result);
        }
        catch (Exception ex)
        {
            Logger?.LogError(LoggerEventId, ex, "Ga4Track could not send its track");
            if (ThrowExceptionOnError)
                throw;
        }
    }

    public void UseSimpleLogger(bool singleLine = false)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(configure =>
            {
                // ReSharper disable once StringLiteralTypo
                configure.TimestampFormat = "[HH:mm:ss.ffff] ";
                configure.IncludeScopes = false;
                configure.SingleLine = singleLine;
            });
        });

        Logger = loggerFactory.CreateLogger("");
    }
}
